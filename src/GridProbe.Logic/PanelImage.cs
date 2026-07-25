namespace GridProbe;

// Screen-resolution resampler for height-field views.
// Zoomed in (cell larger than a pixel): bilinear interpolation for smooth gradients.
// Zoomed out (many cells per pixel): exact area average via a summed-area table —
// the continuous limit of a mip pyramid, so sub-pixel structure is never skipped
// and there is no level-switch popping at any zoom factor.
internal static class PanelImage
{
    public static byte[,] Render(int[,] view, int destW, int destH)
        => Render(view, destW, destH, 0, 0, view.GetLength(0), view.GetLength(1));

    // Window (wx0..wx1, wy0..wy1) is the visible source region in cell coords (zoom/pan).
    public static byte[,] Render(int[,] view, int destW, int destH, double wx0, double wy0, double wx1, double wy1)
    {
        int w = view.GetLength(0), h = view.GetLength(1);

        // Normalize between the grid's own thinnest and thickest occupied
        // columns (over the WHOLE view, not the window, so zoom doesn't pump
        // the exposure) so the full gray ramp is spent on this ship's range.
        int minV = int.MaxValue, maxV = 1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int v = view[x, y];
                if (v <= 0) continue;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
        if (minV == int.MaxValue) minV = 0;
        double range = maxV - minV;

        var gray = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int v = view[x, y];
                if (v <= 0) { gray[x, y] = 0f; continue; }
                double t = range > 0 ? (v - minV) / range : 1.0;
                gray[x, y] = (float)Math.Min(255.0, 40 + 215.0 * Math.Sqrt(t));
            }
        return Resample(gray, w, h, destW, destH, wx0, wy0, wx1, wy1);
    }

    // Resample a prebuilt tone field (mode-mapped alpha bytes) — no tone curve.
    public static byte[,] RenderTones(byte[,] tones, int destW, int destH, double wx0, double wy0, double wx1, double wy1)
    {
        int w = tones.GetLength(0), h = tones.GetLength(1);
        var gray = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                gray[x, y] = tones[x, y];
        return Resample(gray, w, h, destW, destH, wx0, wy0, wx1, wy1);
    }

    private static byte[,] Resample(float[,] gray, int w, int h, int destW, int destH, double wx0, double wy0, double wx1, double wy1)
    {
        double sx = (wx1 - wx0) / destW, sy = (wy1 - wy0) / destH;
        var img = new byte[destW, destH];

        if (sx <= 1.0001 && sy <= 1.0001)
        {
            // Sharpened bilinear: plain bilinear smears each cell edge across a
            // full cell (~2+ px when magnified). Contracting the fractional
            // coordinate narrows transitions to ~1 output pixel while interior
            // gradients stay smooth.
            double k = Math.Clamp(Math.Min(1.0 / sx, 1.0 / sy), 1.0, 4.0);
            for (int py = 0; py < destH; py++)
            {
                double fy = wy0 + (py + 0.5) * sy - 0.5;
                int y0 = (int)Math.Floor(fy);
                double ty = Math.Clamp((fy - y0 - 0.5) * k + 0.5, 0.0, 1.0);
                int y1 = Math.Clamp(y0 + 1, 0, h - 1);
                y0 = Math.Clamp(y0, 0, h - 1);
                for (int px = 0; px < destW; px++)
                {
                    double fx = wx0 + (px + 0.5) * sx - 0.5;
                    int x0 = (int)Math.Floor(fx);
                    double tx = Math.Clamp((fx - x0 - 0.5) * k + 0.5, 0.0, 1.0);
                    int x1 = Math.Clamp(x0 + 1, 0, w - 1);
                    x0 = Math.Clamp(x0, 0, w - 1);
                    double g = gray[x0, y0] * (1 - tx) * (1 - ty) + gray[x1, y0] * tx * (1 - ty)
                             + gray[x0, y1] * (1 - tx) * ty + gray[x1, y1] * tx * ty;
                    img[px, py] = (byte)Math.Clamp(g + 0.5, 0.0, 255.0);
                }
            }
            return img;
        }

        var sat = new double[w + 1, h + 1];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                sat[x + 1, y + 1] = gray[x, y] + sat[x, y + 1] + sat[x + 1, y] - sat[x, y];

        for (int py = 0; py < destH; py++)
        {
            int y0 = Math.Max(0, (int)(wy0 + py * sy));
            int y1 = Math.Min(h, Math.Max(y0 + 1, (int)Math.Ceiling(wy0 + (py + 1) * sy)));
            for (int px = 0; px < destW; px++)
            {
                int x0 = Math.Max(0, (int)(wx0 + px * sx));
                int x1 = Math.Min(w, Math.Max(x0 + 1, (int)Math.Ceiling(wx0 + (px + 1) * sx)));
                double sum = sat[x1, y1] - sat[x0, y1] - sat[x1, y0] + sat[x0, y0];
                img[px, py] = (byte)Math.Clamp(sum / ((x1 - x0) * (y1 - y0)) + 0.5, 0.0, 255.0);
            }
        }
        return img;
    }
}
