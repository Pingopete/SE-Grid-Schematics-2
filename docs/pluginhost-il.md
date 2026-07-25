=== Keen.VRage.Core.Plugins.IPlugin Keen.VRage.Core.Plugins.PluginHost::Add(System.Type)
  ldarg.0      
  ldfld        System.Collections.Generic.List`1<Keen.VRage.Core.Plugins.IPlugin> Keen.VRage.Core.Plugins.PluginHost::Plugins
  callvirt     System.Collections.Generic.List`1/Enumerator<!0> System.Collections.Generic.List`1<Keen.VRage.Core.Plugins.IPlugin>::GetEnumerator()
  stloc.1      
  br.s         IL_0028: ldloca.s V_1
  ldloca.s     V_1
  call         !0 System.Collections.Generic.List`1/Enumerator<Keen.VRage.Core.Plugins.IPlugin>::get_Current()
  stloc.2      
  ldloc.2      
  callvirt     System.Type System.Object::GetType()
  ldarg.1      
  call         System.Boolean System.Type::op_Equality(System.Type,System.Type)
  brfalse.s    IL_0028: ldloca.s V_1
  ldloc.2      
  stloc.3      
  leave.s      IL_0077: ldloc.3
  ldloca.s     V_1
  call         System.Boolean System.Collections.Generic.List`1/Enumerator<Keen.VRage.Core.Plugins.IPlugin>::MoveNext()
  brtrue.s     IL_000e: ldloca.s V_1
  leave.s      IL_0041: nop
  ldloca.s     V_1
  constrained. System.Collections.Generic.List`1/Enumerator<Keen.VRage.Core.Plugins.IPlugin>
  callvirt     System.Void System.IDisposable::Dispose()
  endfinally   
  nop          
  ldarg.1      
  ldc.i4.1     
  newarr       System.Object
  dup          
  ldc.i4.0     
  ldarg.0      
  stelem.ref   
  call         System.Object System.Activator::CreateInstance(System.Type,System.Object[])
  castclass    Keen.VRage.Core.Plugins.IPlugin
  stloc.0      
  leave.s      IL_0069: ldarg.0
  pop          
  ldarg.1      
  call         System.Object System.Activator::CreateInstance(System.Type)
  castclass    Keen.VRage.Core.Plugins.IPlugin
  stloc.0      
  leave.s      IL_0069: ldarg.0
  ldarg.0      
  ldfld        System.Collections.Generic.List`1<Keen.VRage.Core.Plugins.IPlugin> Keen.VRage.Core.Plugins.PluginHost::Plugins
  ldloc.0      
  callvirt     System.Void System.Collections.Generic.List`1<Keen.VRage.Core.Plugins.IPlugin>::Add(!0)
  ldloc.0      
  ret          
  ldloc.3      
  ret          

=== Keen.VRage.Core.Plugins.IPlugin Keen.VRage.Core.Plugins.PluginHost::TryAddFromAssembly(System.String)
  newobj       System.Void Keen.VRage.Core.Plugins.PluginHost/<>c__DisplayClass11_0::.ctor()
  stloc.0      
  ldarg.1      
  ldstr        .dll
  callvirt     System.Boolean System.String::EndsWith(System.String)
  brfalse.s    IL_0021: ldarg.1
  ldloc.0      
  ldarg.1      
  call         System.Reflection.Assembly System.Reflection.Assembly::LoadFrom(System.String)
  stfld        System.Reflection.Assembly Keen.VRage.Core.Plugins.PluginHost/<>c__DisplayClass11_0::assembly
  br.s         IL_0042: call System.AppDomain System.AppDomain::get_CurrentDomain()
  ldarg.1      
  ldstr        .csproj
  callvirt     System.Boolean System.String::EndsWith(System.String)
  brfalse.s    IL_0036: ldloc.0
  ldarg.1      
  call         System.String System.IO.Path::GetFileNameWithoutExtension(System.String)
  starg.s      assemblyName
  ldloc.0      
  ldarg.1      
  call         System.Reflection.Assembly System.Reflection.Assembly::Load(System.String)
  stfld        System.Reflection.Assembly Keen.VRage.Core.Plugins.PluginHost/<>c__DisplayClass11_0::assembly
  call         System.AppDomain System.AppDomain::get_CurrentDomain()
  stloc.1      
  ldloc.1      
  ldloc.0      
  ldftn        System.Reflection.Assembly Keen.VRage.Core.Plugins.PluginHost/<>c__DisplayClass11_0::<TryAddFromAssembly>g__LoadFromAssemblyLocation|0(System.Object,System.ResolveEventArgs)
  newobj       System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)
  callvirt     System.Void System.AppDomain::add_AssemblyResolve(System.ResolveEventHandler)
  leave.s      IL_00ac: ldarg.0
  stloc.2      
  ldarg.0      
  ldc.i4.1     
  stfld        System.Boolean Keen.VRage.Core.Plugins.PluginHost::_failedPluginLoad
  call         Keen.VRage.Library.Diagnostics.Log Keen.VRage.Library.Diagnostics.Log::get_Default()
  ldloca.s     V_3
  ldc.i4.s     19
  ldc.i4.3     
  call         System.Void System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::.ctor(System.Int32,System.Int32)
  ldloca.s     V_3
  ldstr        Plugin NOT loaded: 
  call         System.Void System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::AppendLiteral(System.String)
  ldloca.s     V_3
  ldarg.1      
  call         System.Void System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::AppendFormatted(System.String)
  ldloca.s     V_3
  call         System.String System.Environment::get_NewLine()
  call         System.Void System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::AppendFormatted(System.String)
  ldloca.s     V_3
  ldloc.2      
  call         System.Void System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::AppendFormatted<System.Exception>(!!0)
  ldloca.s     V_3
  call         System.String System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::ToStringAndClear()
  callvirt     System.Void Keen.VRage.Library.Diagnostics.Log::WriteLine(System.String)
  ldnull       
  stloc.s      V_4
  leave.s      IL_00b9: ldloc.s V_4
  ldarg.0      
  ldloc.0      
  ldfld        System.Reflection.Assembly Keen.VRage.Core.Plugins.PluginHost/<>c__DisplayClass11_0::assembly
  call         Keen.VRage.Core.Plugins.IPlugin Keen.VRage.Core.Plugins.PluginHost::TryAddFromAssembly(System.Reflection.Assembly)
  ret          
  ldloc.s      V_4
  ret          

=== Keen.VRage.Core.Plugins.IPlugin Keen.VRage.Core.Plugins.PluginHost::TryAddFromAssembly(System.Reflection.Assembly)
  ldarg.1      
  callvirt     System.Type[] System.Reflection.Assembly::GetTypes()
  stloc.0      
  ldc.i4.0     
  stloc.1      
  br.s         IL_004a: ldloc.1
  ldloc.0      
  ldloc.1      
  ldelem.ref   
  stloc.2      
  ldtoken      Keen.VRage.Core.Plugins.IPlugin
  call         System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)
  ldloc.2      
  callvirt     System.Boolean System.Type::IsAssignableFrom(System.Type)
  brfalse.s    IL_0046: ldloc.1
  ldloc.2      
  callvirt     System.Boolean System.Type::get_IsAbstract()
  brtrue.s     IL_0046: ldloc.1
  call         !0 Keen.VRage.Library.Utils.Singleton`1<Keen.VRage.Library.Reflection.MetadataManager>::get_Instance()
  ldc.i4.1     
  newarr       System.Reflection.Assembly
  dup          
  ldc.i4.0     
  ldarg.1      
  stelem.ref   
  callvirt     Keen.VRage.Library.Reflection.IMetadataContext Keen.VRage.Library.Reflection.MetadataManager::PushContext(System.Reflection.Assembly[])
  pop          
  ldarg.0      
  ldloc.2      
  call         Keen.VRage.Core.Plugins.IPlugin Keen.VRage.Core.Plugins.PluginHost::Add(System.Type)
  ret          
  ldloc.1      
  ldc.i4.1     
  add          
  stloc.1      
  ldloc.1      
  ldloc.0      
  ldlen        
  conv.i4      
  blt.s        IL_000b: ldloc.0
  ldnull       
  ret          

=== System.Void Keen.VRage.Core.Plugins.PluginHost::LoadPluginsFromArgs()
  ldarg.0      
  ldfld        System.String[] Keen.VRage.Core.Plugins.PluginHost::Args
  ldsfld       System.Func`2<System.String,System.Boolean> Keen.VRage.Core.Plugins.PluginHost/<>c::<>9__18_0
  dup          
  brtrue.s     IL_0025: call !!0 System.Linq.Enumerable::FirstOrDefault<System.String>(System.Collections.Generic.IEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>)
  pop          
  ldsfld       Keen.VRage.Core.Plugins.PluginHost/<>c Keen.VRage.Core.Plugins.PluginHost/<>c::<>9
  ldftn        System.Boolean Keen.VRage.Core.Plugins.PluginHost/<>c::<LoadPluginsFromArgs>b__18_0(System.String)
  newobj       System.Void System.Func`2<System.String,System.Boolean>::.ctor(System.Object,System.IntPtr)
  dup          
  stsfld       System.Func`2<System.String,System.Boolean> Keen.VRage.Core.Plugins.PluginHost/<>c::<>9__18_0
  call         !!0 System.Linq.Enumerable::FirstOrDefault<System.String>(System.Collections.Generic.IEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>)
  stloc.0      
  ldloc.0      
  call         System.Boolean System.String::IsNullOrEmpty(System.String)
  brtrue.s     IL_0064: ret
  ldarg.0      
  ldloc.0      
  ldstr        -plugins:
  call         System.Int32 System.String::get_Length()
  callvirt     System.String System.String::Substring(System.Int32)
  call         System.Boolean Keen.VRage.Core.Plugins.PluginHost::LoadPlugins(System.String)
  brfalse.s    IL_0064: ret
  call         !0 Keen.VRage.Library.Utils.Singleton`1<Keen.VRage.Core.Platform.CrashReporting.CrashHandler>::get_Instance()
  ldsfld       System.String Keen.VRage.Core.Platform.CrashReporting.CrashTags::PluginLoadedTag
  ldloca.s     V_1
  initobj      System.Nullable`1<Keen.VRage.Library.Localization.LocKey>
  ldloc.1      
  ldc.i4.0     
  callvirt     System.Void Keen.VRage.Core.Platform.CrashReporting.CrashHandler::RegisterProcessTag(System.String,System.Nullable`1<Keen.VRage.Library.Localization.LocKey>,System.Boolean)
  ret          

=== System.Boolean Keen.VRage.Core.Plugins.PluginHost::LoadPlugins(System.String)
  ldc.i4.0     
  stloc.0      
  ldarg.1      
  ldc.i4.s     59
  ldc.i4.1     
  callvirt     System.String[] System.String::Split(System.Char,System.StringSplitOptions)
  stloc.1      
  ldc.i4.0     
  stloc.2      
  br.s         IL_0023: ldloc.2
  ldloc.1      
  ldloc.2      
  ldelem.ref   
  stloc.3      
  ldarg.0      
  ldloc.3      
  call         Keen.VRage.Core.Plugins.IPlugin Keen.VRage.Core.Plugins.PluginHost::TryAddFromAssembly(System.String)
  brfalse.s    IL_001f: ldloc.2
  ldc.i4.1     
  stloc.0      
  ldloc.2      
  ldc.i4.1     
  add          
  stloc.2      
  ldloc.2      
  ldloc.1      
  ldlen        
  conv.i4      
  blt.s        IL_0010: ldloc.1
  ldloc.0      
  ret          

=== System.Void Keen.VRage.Core.Plugins.PluginHost::PostEngineInit(Keen.VRage.DCS.Components.Entity)
  ldarg.0      
  ldfld        System.Boolean Keen.VRage.Core.Plugins.PluginHost::_failedPluginLoad
  brfalse.s    IL_002f: ret
  ldarg.1      
  ldnull       
  call         !!0 Keen.VRage.DCS.Components.EntityFunctions::Single<Keen.VRage.Core.Platform.IPlatformWindows>(Keen.VRage.DCS.Components.Entity,System.Predicate`1<!!0>)
  ldsfld       Keen.VRage.Library.Localization.LocKey Keen.VRage.Core.Localization.EngineTexts::IncorrectPluginPath
  ldsfld       Keen.VRage.Library.Localization.LocKey Keen.VRage.Core.Localization.EngineTexts::InvalidArguments
  ldc.i4.0     
  ldnull       
  callvirt     Keen.VRage.Core.Platform.MessageBoxResult Keen.VRage.Core.Platform.IPlatformWindows::MessageBoxLocalized(Keen.VRage.Library.Localization.LocKey,Keen.VRage.Library.Localization.LocKey,Keen.VRage.Core.Platform.MessageBoxOptions,System.Collections.Generic.Dictionary`2<System.String,System.Object>)
  stloc.0      
  ldloc.0      
  ldc.i4.1     
  bne.un.s     IL_002f: ret
  call         !0 Keen.VRage.Library.Utils.Singleton`1<Keen.VRage.Core.VRageCore>::get_Instance()
  callvirt     System.Void Keen.VRage.Core.VRageCore::Exit()
  ret          

=== System.Void Keen.VRage.Core.Plugins.PluginHost::InvokeOnBeforeEngineInstantiated(Keen.VRage.Core.EngineComponents.EngineBuilder)
  ldarg.0      
  ldfld        System.Action`1<Keen.VRage.Core.EngineComponents.EngineBuilder> Keen.VRage.Core.Plugins.PluginHost::OnBeforeEngineInstantiated
  dup          
  brtrue.s     IL_000c: ldarg.1
  pop          
  br.s         IL_0012: ldarg.0
  ldarg.1      
  callvirt     System.Void System.Action`1<Keen.VRage.Core.EngineComponents.EngineBuilder>::Invoke(!0)
  ldarg.0      
  ldnull       
  stfld        System.Action`1<Keen.VRage.Core.EngineComponents.EngineBuilder> Keen.VRage.Core.Plugins.PluginHost::OnBeforeEngineInstantiated
  ret          

=== System.Void Keen.VRage.Core.Plugins.PluginHost::InvokeOnBeforeProjectsLoaded(System.Collections.Generic.List`1<Keen.VRage.Core.Project.VRageProject>)
  ldarg.0      
  ldfld        System.Action`1<System.Collections.Generic.List`1<Keen.VRage.Core.Project.VRageProject>> Keen.VRage.Core.Plugins.PluginHost::OnBeforeProjectsLoaded
  dup          
  brtrue.s     IL_000c: ldarg.1
  pop          
  br.s         IL_0012: ldarg.0
  ldarg.1      
  callvirt     System.Void System.Action`1<System.Collections.Generic.List`1<Keen.VRage.Core.Project.VRageProject>>::Invoke(!0)
  ldarg.0      
  ldnull       
  stfld        System.Action`1<System.Collections.Generic.List`1<Keen.VRage.Core.Project.VRageProject>> Keen.VRage.Core.Plugins.PluginHost::OnBeforeProjectsLoaded
  ret          

=== System.Void Keen.VRage.Core.Plugins.PluginHost::.ctor(System.String[])
  ldarg.0      
  newobj       System.Void System.Collections.Generic.List`1<Keen.VRage.Core.Plugins.IPlugin>::.ctor()
  stfld        System.Collections.Generic.List`1<Keen.VRage.Core.Plugins.IPlugin> Keen.VRage.Core.Plugins.PluginHost::Plugins
  ldarg.0      
  call         System.Void System.Object::.ctor()
  ldarg.0      
  ldarg.1      
  stfld        System.String[] Keen.VRage.Core.Plugins.PluginHost::Args
  ldarg.0      
  call         System.Void Keen.VRage.Core.Plugins.PluginHost::LoadDevPlugins()
  ldarg.0      
  call         System.Void Keen.VRage.Core.Plugins.PluginHost::LoadPluginsFromArgs()
  ret          

