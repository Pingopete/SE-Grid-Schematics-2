== Keen.VRage.DCS.Scenes.Scene (base: System.Object)
  ctor(List`1 jobs, List`1 jobGroupsOrdered, Dictionary`2 jobSystemsIndex) 
  prop SceneDebuggerContext DebuggerContext
  prop Object UserObject
  prop String DebugName
  prop JobCommandBuffer JobDataCollector
  prop EntitySerializer EntitySerializer
  prop IEntityLifetimeTracker EntityLifetimeTracker
  SynchronizationContextToken ActivateSceneSyncContextForCurrentThread()
  ContinuationModificationToken`1 ExecuteInParallel(Boolean runOnCurrentThread, Nullable`1 parallelDeadline)
  Boolean IsCurrentThreadDCS()
  AwaitToken MoveToDCS()
  DCSToken MoveToDCS()
  ParallelToken MoveToParallel()
  Task ComputeDuring(Type jobSystem, TInput input, Func`2 taskFactory, Entity entity)
  Task FinishDuring(Task& task)
  Task FinishBefore(Task& task)
  Void FlushAllContinuations()
  CBToken OpenCommandBufferIfNeeded(Boolean dataPrecise)
  Void AssertDataPreciseContext()
  Void Dispose()
  DEntityArchetype GetArchetype(Span`1 typeIds)
  DEntityArchetype GetArchetypeOf(DEntity entity)
  DEntity AddEntity(Nullable`1 entityArchetype, DEntity preallocatedId)
  Void RemoveEntity(DEntity entity)
  Boolean TryRemoveData(DEntity entity, Int32 typeId)
  Void Tick(Boolean doSync)
  Void DoSync()
  ReaderToken OpenNoSyncSection()
  Void AssertEntityAlive(DEntity entity, EntityAliveStates acceptedStates)
  Boolean IsEntityAlive(DEntity entity, EntityAliveStates acceptedStates)
  Void AssertDuring(ParallelThreadSequenceBehavior parallel)
  Void AssertOutsideOf(ParallelThreadSequenceBehavior parallel)
  Void AssertBefore(ParallelThreadSequenceBehavior parallel)
  Void AssertAfter(ParallelThreadSequenceBehavior parallel)
  Boolean IsCurrentStageInside(ParallelThreadSequenceBehavior parallel)
  Boolean IsCurrentStageOutsideOf(ParallelThreadSequenceBehavior parallel)
  Boolean IsCurrentStageBefore(ParallelThreadSequenceBehavior parallel)
  Boolean IsCurrentStageAfter(ParallelThreadSequenceBehavior parallel)
  ValueTuple`2 GetSequenceIndicesOf()
  ValueTuple`2 GetSequenceIndicesOf(Type jobGroup)
  DEntityArchetype GetArchetype(ReadOnlySpan`1 types)
  Void PreallocateEntityIndex(Int32 requiredCapacity)
  IEnumerable`1 EnumerateEntities()
  IEnumerable`1 EnumerateArchetypes(Boolean includeConcurrent)
  Void QueryArchetypes(ReadOnlySpan`1 mustHaveAndMustNotHaveIds, Int32 mustHaveCount, BufferReference`1 archetypesOut)
  DEntityContext AddEntity(Type[] types)
  DEntityContext AddEntity(T1 data1)
  DEntityContext AddEntity(T1 data1, T2 data2)
  DEntityContext AddEntity(T1 data1, T2 data2, T3 data3)
  DEntityContext AddEntity(T1 data1, T2 data2, T3 data3, T4 data4)
  Stats GetStats(Stats stats, Boolean syncBeforeCounting)
  Object& JobContextFor(Int32 jobId)
  Void SetJobEnabled(Int32 jobId, Boolean enabled, RestartArgs args)

== Keen.VRage.DCS.Components.Entity (base: System.Object)
  ctor() [nonpublic]
  ctor(Entity& instance, CloningContext& context) [nonpublic]
  prop DEntityContext Data
  prop Scene Scene
  prop DEntity DEntity
  prop String DebugName
  prop CompositionData CompositionData
  T Get(StringId tag)
  Component Get(StringId tag)
  T TryGet(StringId tag)
  Component TryGet(StringId tag)
  Boolean Has(TFeature& feature, Func`2 conditional)
  SpanEnumerable`3 All(Predicate`1 conditional)
  Void ForEach(Action`1 action, Boolean reverse)
  Boolean Equals(Entity other)
  static Entity FromDataEntity(DEntityContext dataEntity)
  static Entity TryGetFromDataEntity(DEntityContext dataEntity)
  EntityCompositeDefinition GetComposition()
  Entity DeepClone()
  Entity DeepClone(CloningContext& context)

== Keen.VRage.DCS.Components.Component (base: System.Object)
  ctor() 
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data

== Keen.Game2.Simulation.WorldObjects.CubeGrids.CubeGridComponent (base: Keen.VRage.Core.Game.Components.GameComponent)
  ctor() 
  prop Boolean HasActiveParkingBrake
  prop ListReader`1 ParkingBrakes
  prop ICubeBlockStorage CubeBlockStorage
  prop Boolean SpawnedFromSplit
  prop Boolean RemovedByManagedArea
  prop Boolean GridIsTurnedOn
  prop Boolean LightsAreTurnedOn
  prop GridParkingState ParkingState
  prop LocalizableText DisplayName
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Void VisitAllBlocksWithComponent(TVisitor& visitor, Boolean includeSubgrids)
  Void VisitAllBlocksWithComponent(Action`1 visitor, Boolean includeSubgrids)
  NetworkStory TogglePower(SkipOnReplicationFail`1 controlledEntity)
  Void SetGridPower(Boolean value)
  NetworkStory ToggleDampeners(SkipOnReplicationFail`1 controlledEntity)
  NetworkStory ToggleLights(SkipOnReplicationFail`1 controlledEntity)
  NetworkStory ToggleParkingBrakes(SkipOnReplicationFail`1 controlledEntity)
  Void GridsConnectionChanged(CubeGridComponent gridThis, CubeGridComponent gridOther, ImmutableArray`1 definitionConnectionGroups, Boolean connected)
  Void LogEvent(String data)
  Void DumpEventLog()
  ConnectionGroupDefinition GetTerminalConnectionType()
  IEnumerable`1 GetAllGridsInShip()
  Void RemoveBlock(CubeBlockComponent block)
  Entity GetBlock(Vector3I position)
  Entity GetBlockWithComponent(T& component)
  WorldTransform GetWorldTransform(Vector3I blockPosition)
  static CubeBlockComponent TryGetClosestIntersectedBlock(Span`1 blocks, Ray localRay, Vector3I& hitPosition, Single& hitDistanceFraction)
  Nullable`1 TryGetClosestIntersectedBlock(Vector3 localStartPoint, Vector3 localEndPoint, Single& intersectionDistanceFraction, CubeBlockComponent& intersectedBlock)
  Boolean IsAreaEmpty(BoundingBoxI area)
  Void CommitBlockChanges()
  Boolean IsConnected(ConnectionGroupDefinition type, CubeGridComponent otherGrid)
  Boolean IsAreaFreeOfBlocksBeingAdded(BoundingBoxI aabb)
  Void BlockDestroyed(CubeBlockComponent block)
  Void BlockColliderChanged(CubeBlockComponent block)
  Void BlockHealthChanged(CubeBlockComponent block)
  Void BlockBuildProgressChanged(CubeBlockComponent block)
  Void SetRemovedByManagedArea()

== Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockComponent (base: Keen.VRage.Core.Game.Components.GameComponent)
  ctor() 
  prop CubeGridComponent Grid
  prop BoundingBoxI AABB
  prop IntegerOrientation BlockOrientation
  prop BoundingBox LocalBounds
  prop Boolean EphemeralFirstItem
  prop Single AbsoluteHealth
  prop Single EffectiveIntegrity
  prop Single EffectiveBuildProgress
  prop Single EffectivePermeability
  prop CubeBlockDefinition Definition
  prop Nullable`1 Color
  prop Single HealthIntegrity
  prop Single BuildProgress
  prop Boolean Generated
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Void DealDamage(BlockDamageInfo damageInfo)
  Void OnBeforeTakingDamage(BlockDamageInfo damageInfo)
  Void OnAfterTakingDamage(BlockDamageInfo damageInfo)
  Void OnDestroyed()
  Vector3I GetFirstOccupiedCell()
  BoundingBoxI GetTransformedOccupiedCellGroup(Int32 index)
  OccupiedGridCellsEnumerator GetTransformedOccupiedCellGroups()
  Vector3I GetMountPointsPositionOffset()
  Boolean CanConnectBlock(BoundingBoxI otherBlockBB, Direction direction)
  Boolean IsMountPointEnabled(MountPointsGroupData mountPoint)
  IntegrityChangeResults ChangeBuildProgress(Single changeRatio)
  Void GetStoredItems(BufferReference`1 itemsOut, Boolean includeZeroItems)
  Void GetTotalItems(BufferReference`1 itemsOut, Boolean includeZeroItems)
  Void GetItemsDelta(BufferReference`1 itemDelta, Single& progressDiff)
  Boolean IsMountPointAvailable(Vector3I mountPosition, Direction mountDirection)
  Boolean Equals(CubeBlockComponent other)

== Keen.Game2.Simulation.WorldObjects.CubeGrids.BlockOctrees.BlockOctreeComponent (base: Keen.VRage.DCS.Components.Component)
  ctor() 
  prop Boolean ComputeConsistency
  prop BoundingBoxI Boundary
  prop Int32 CubeBlockCount
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Span`1 GetAllCubeBlocks()
  Void GetCubeBlocks(BoundingBoxI queryRange, BufferReference`1 blocksOut)
  Void GetConnectedCubeBlocks(BoundingBoxI queryRange, Vector3I targetPosition, CubeBlockComponent targetBlock, BufferReference`1 blocksOut)
  Void InsertRange(ReadOnlySpan`1 cubeBlocks)
  Void UpdateBlockChanges(BlocksChangedArgs changedBlocks)
  Boolean IsAreaEmpty(BoundingBoxI queryRange)
  CubeBlockComponent TryGetCubeBlock(Vector3I gridPosition)
  Void Remove(ReadOnlySpan`1 cubeBlocks)
  Boolean HasBlockConnection(Vector3I startPosition, Direction direction)
  Void UpdateGridConsistency(Boolean isConsistencyDirty, Boolean disconnectBlocks)
  Boolean CanTraverse(Vector3I startPosition, Direction direction)

== Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd.LcdMultiPanelComponent (base: Keen.VRage.Core.Game.Components.GameComponent)
  ctor() 
  prop LcdMultiPanelDefinition Definition
  prop Int32 SurfaceCount
  prop LcdPanelSurfaceState[] SurfaceStates
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  LcdPanelSurfaceState& GetSurfaceState(Int32 surfaceIndex)
  Void SetSurfaceState(Int32 surfaceIndex, LcdPanelSurfaceState& state)
  Void SetSurfaceImages(Int32 surfaceIndex, ResourceHandle`1[] images)
  Void SetSurfaceImageInterval(Int32 surfaceIndex, Single seconds)
  Void SetSurfaceText(Int32 surfaceIndex, String text)
  Void SetSurfaceDisplayName(Int32 surfaceIndex, String displayName)
  String GetSurfaceEffectiveDisplayName(Int32 surfaceIndex)
  Void SetSurfaceBackgroundColor(Int32 surfaceIndex, ColorSRGB color)
  Void SetSurfaceTextColor(Int32 surfaceIndex, ColorSRGB color)
  Void SetSurfaceFont(Int32 surfaceIndex, ResourceHandle`1 font)
  Void SetSurfaceFontSize(Int32 surfaceIndex, Single fontSize)
  Void SetSurfaceHorizontalAlignment(Int32 surfaceIndex, HorizontalAlignment alignment)
  Void SetSurfaceVerticalAlignment(Int32 surfaceIndex, VerticalAlignment alignment)
  Void SetSurfacePadding(Int32 surfaceIndex, Single padding)
  Void SetSurfaceContent(Int32 surfaceIndex, LcdPanelContent content)
  Void SetSurfacePreserveAspectRatio(Int32 surfaceIndex, Boolean preserveAspectRatio)
  Void SetSurfaceOrientation(Int32 surfaceIndex, LcdScreenOrientation orientation)

== Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd.LcdPanelSurfaceState (base: System.ValueType)
  ctor() 
  ctor(LcdPanelSurfaceState& instance, CloningContext& context) [nonpublic]
  static IStreamSerializer`1 GetStreamSerializer(SerializerFormat format)
  LcdPanelSurfaceState DeepClone()
  LcdPanelSurfaceState DeepClone(CloningContext& context)
  AccessibleTypeInfo GetTypeInfo()

== Keen.VRage.Physics.IPhysics (base: )
  prop SolverSettings SolverSettings
  prop Single StepDelta
  prop Single GravityMultiplier
  prop Vector3 GlobalGravity
  prop IPhysicsSessionDebug Debug
  prop IPhysicsColliders Colliders
  prop IRagdolls Ragdolls
  prop IPhysicsWorldManager WorldManager
  prop IMaterialLibrary Materials
  prop IPhysicsConstraints Constraints
  IPhysicsCollider AllocateCollider(TArgs& args)
  Boolean HasBody(DEntity entity)
  Void AllocateBody(DEntity entity, BodyArgs& body)
  Void SetBodyCollider(DEntity body, IPhysicsCollider collider)
  IPhysicsCollider GetBodyCollider(DEntity body)
  Void SetBodyRagdoll(DEntity body, BodyRagdollArgs& args)
  Void RemoveBodyRagdoll(DEntity body)
  Void SetBodyRagdollEntityLayer(DEntity entity, StringId entityLayer)
  Void SetBodyMotionType(DEntity body, Motion motionType)
  Void SetBodyQuality(DEntity body, Quality quality)
  Boolean AreBodiesConnected(DEntity bodyA, DEntity bodyB)
  Boolean AreBodiesConnected(DEntity bodyA, DEntity bodyB)
  Boolean GetDirectlyConnectedBodies(DEntity body, BufferReference`1 bodiesOut)
  Boolean GetDirectlyConnectedBodies(DEntity body, BufferReference`1 bodiesOut)
  Boolean AreBodiesSharingMotion(DEntity bodyA, DEntity bodyB)
  MaterialId GetBodyMaterial(DEntity body, SubColliderKey subCollider)
  MaterialId GetBodyMaterial(DEntity body)
  Void DestroyBody(DEntity entity)
  ICharacterStateMachine AllocateCharacterStateMachine(CharacterStateMachineArgs& args)
  Void AllocateCharacterController(DEntity entity, CharacterControllerArgs& args)
  Void AllocateTrigger(DEntity entity, TriggerArgs& args)
  Void SetTriggerFilter(DEntity triggerEntity, EntityLayerFilter filter)
  Void DestroyTrigger(DEntity entity)
  IPhysicsConstraint AllocateConstraint(DEntity bodyA, DEntity bodyB, IConstraintArgs args)
  IConstraintMotor AllocateMotor(TArgs& args)
  Void DestroyConstraint(IPhysicsConstraint constraint)
  Void CollectActiveConnectedBodies(DEntity body, BufferReference`1 otherBodiesOut)
  Void CastRay(BufferReference`1 hits, RayCastArgs& args, CollisionPreset collisionPreset)
  Void CastAABB(BufferReference`1 hits, AABBCastArgs& args, CollisionPreset collisionPreset)
  Void CastCollider(BufferReference`1 hits, ColliderCastArgs& args, CollisionPreset collisionPreset)
  Void QueryPoint(BufferReference`1 hits, PointQueryArgs& args, CollisionPreset collisionPreset)
  Void QueryAABB(BufferReference`1 hits, AABBQueryArgs& args, CollisionPreset collisionPreset)
  Void QueryCollider(BufferReference`1 hits, ColliderQueryArgs& args, CollisionPreset collisionPreset)
  Task`1 CastRayAsync(RayCastArgs& args, CollisionPreset collisionPreset)
  Task`1 CastAABBAsync(AABBCastArgs& args, CollisionPreset collisionPreset)
  Task`1 CastColliderAsync(ColliderCastArgs& args, CollisionPreset collisionPreset)
  Task`1 QueryPointAsync(PointQueryArgs& args, CollisionPreset collisionPreset)
  Task`1 QueryAABBAsync(AABBQueryArgs& args, CollisionPreset collisionPreset)
  Task`1 QueryColliderAsync(ColliderQueryArgs& args, CollisionPreset collisionPreset)
  Void AttachEntities(Buffer`1 entitiesToAttach, DEntity target)
  Void DetachEntities(Buffer`1 entitiesToDetach)
  Void SetManifoldCollector(IManifoldCollector collector)
  Void SetCollisionSphereCollector(ICollisionSphereCollector collector)
  Void RemoveManifoldCollector()
  Void RemoveCollisionSphereCollector()
  Boolean TryGetEntityLayer(DEntity entity, StringId& layer)
  Boolean MatchesLayerFilter(DEntity entity, EntityLayerFilter filter)
  Void SetEntityLayer(DEntity entity, StringId layer)
  Void SetManifoldInvInertiaMultiplier(DEntity entity, Vector3 invInertiaMultiplier, Single invMassMultiplier)
  Void ClearManifoldInvInertiaMultiplier(DEntity entity)
  Nullable`1 GetManifoldInvInertiaMultiplier(DEntity entity)
  Int32 AllocateCollisionSystemGroupId()
  Void CollectStatistics(BufferReference`1 statistics, Int32 maxDepth)

== Keen.VRage.Core.Game.Components.SessionComponent (base: Keen.VRage.DCS.Components.Component)
  ctor() [nonpublic]
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data

== Keen.VRage.Core.Game.Components.SessionComponentFunctions (base: System.Object)
  static Void TrackEntities(SessionComponent context, HashSet`1 entities)
  static Void TrackEntities(SessionComponent context, Action`1 onEntityAdded, Action`1 onEntityRemoved)

== Keen.VRage.Core.Plugins.PluginHost (base: System.Object)
  ctor(String[] args) 
  Void Dispose()
  IPlugin TryAddFromAssembly(String assemblyName)
  IPlugin TryAddFromAssembly(Assembly assembly)
  IPlugin Add(Type pluginType)
  Void InvokeOnBeforeEngineInstantiated(EngineBuilder engine)
  Void InvokeOnBeforeProjectsLoaded(List`1 pluginsProjects)
  Void PostEngineInit(Entity engine)

== Keen.VRage.Render.Contracts.IDrawBatch (base: )
  prop RenderDrawCommandBuffer CommandBuffer
  Void Submit()
  Void DrawString(Font font, Vector2 screenCoord, ColorSRGB colorMask, String text, Single screenScale, Boolean ignoreBounds, Nullable`1 maxTextWidth, Single rotation)
  Void DrawSubstring(Font font, Vector2 screenCoord, ColorSRGB colorMask, ReadOnlySpan`1 text, Single screenScale, Boolean ignoreBounds, Nullable`1 maxTextWidth)
  Void DrawStringAligned(Font font, Vector2 screenCoord, ColorSRGB colorMask, String text, Single fontScale, Boolean ignoreBounds, Nullable`1 maxTextWidth, TextAlignmentEnum align)
  Void DrawStringAligned3D(Font font, Vector3 textCoord, ColorSRGB colorMask, String text, Single fontScale, Boolean ignoreBounds, Nullable`1 rootEntity, Nullable`1 maxTextWidth, TextAlignmentEnum align)
  Void DrawLine(Vector2 from, Vector2 to, ColorSRGB color, Single width, DashingTypeEnum dashingType, Single dashingScale, Boolean ignoreBounds)
  Void DrawPath(ReadOnlySpan`1 splines, ColorSRGB strokeColor, Single strokeWidth, Boolean ignoreBounds)
  Void DrawPathExt(ReadOnlySpan`1 splines, ColorSRGB strokeColor, Single strokeWidth, ReadOnlySpan`1 dashesAndGaps, Single dashOffset, LineCapEnum lineCap, LineJoinEnum lineJoin, Single miterLimit, Boolean ignoreBounds)
  Void DrawFill(ReadOnlySpan`1 splines, ColorSRGB primaryColor, Nullable`1 gradientFill, Boolean ignoreBounds)
  Void DrawImage(ResourceHandle texture, BoundingBox2& destination, ColorSRGB color, Boolean ignoreBounds, Nullable`1 maskTexture, Nullable`1& sourceRectangle)
  Void DrawImageExt(ResourceHandle texture, BoundingBox2& destination, ColorSRGB color, Vector2 rotationPivot, Single rotation, Boolean ignoreBounds, Single rotationSpeed, Nullable`1 maskTexture, Nullable`1& sourceRectangle)
  Void DrawVideoExt(RenderId videoPlayerRenderId, BoundingBox2I& destination)
  Void ScissorPush(BoundingBox2I screenRectangle)
  Void ScissorPop()

== Keen.VRage.Library.Mathematics.QuadraticBezier2 (base: System.ValueType)
  ctor(Vector2 from, Vector2 to) 
  ctor(Vector2 from, Vector2 to, Vector2 control) 
  ctor(QuadraticBezier2& instance, CloningContext& context) [nonpublic]
  BoundingBox2 GetBoundingBox()
  Single GetContourLength()
  static IStreamSerializer`1 GetStreamSerializer(SerializerFormat format)
  QuadraticBezier2 DeepClone()
  QuadraticBezier2 DeepClone(CloningContext& context)
  AccessibleTypeInfo GetTypeInfo()

== Keen.VRage.Library.Mathematics.ColorSRGB (base: System.ValueType)
  ctor(Byte r, Byte g, Byte b, Byte a) 
  ctor(Int32 r, Int32 g, Int32 b, Int32 a) 
  ctor(Single rgb) 
  ctor(Single r, Single g, Single b, Single a) 
  ctor(ColorSRGB color, Single a) 
  ctor(UInt32 packedValue) [nonpublic]
  ctor(ColorSRGB& instance, CloningContext& context) [nonpublic]
  prop ColorSRGB Transparent
  prop ColorSRGB AliceBlue
  prop ColorSRGB AntiqueWhite
  prop ColorSRGB Aqua
  prop ColorSRGB Aquamarine
  prop ColorSRGB Azure
  prop ColorSRGB Beige
  prop ColorSRGB Bisque
  prop ColorSRGB Black
  prop ColorSRGB BlanchedAlmond
  prop ColorSRGB Blue
  prop ColorSRGB BlueViolet
  prop ColorSRGB Brown
  prop ColorSRGB BurlyWood
  prop ColorSRGB CadetBlue
  prop ColorSRGB Chartreuse
  prop ColorSRGB Chocolate
  prop ColorSRGB Coral
  prop ColorSRGB CornflowerBlue
  prop ColorSRGB Cornsilk
  prop ColorSRGB Crimson
  prop ColorSRGB Cyan
  prop ColorSRGB DarkBlue
  prop ColorSRGB DarkCyan
  prop ColorSRGB DarkGoldenrod
  prop ColorSRGB DarkGray
  prop ColorSRGB DarkGreen
  prop ColorSRGB DarkKhaki
  prop ColorSRGB DarkMagenta
  prop ColorSRGB DarkOliveGreen
  prop ColorSRGB DarkOrange
  prop ColorSRGB DarkOrchid
  prop ColorSRGB DarkRed
  prop ColorSRGB DarkSalmon
  prop ColorSRGB DarkSeaGreen
  prop ColorSRGB DarkSlateBlue
  prop ColorSRGB DarkSlateGray
  prop ColorSRGB DarkTurquoise
  prop ColorSRGB DarkViolet
  prop ColorSRGB DeepPink
  prop ColorSRGB DeepSkyBlue
  prop ColorSRGB DimGray
  prop ColorSRGB DodgerBlue
  prop ColorSRGB Firebrick
  prop ColorSRGB FloralWhite
  prop ColorSRGB ForestGreen
  prop ColorSRGB Fuchsia
  prop ColorSRGB Gainsboro
  prop ColorSRGB GhostWhite
  prop ColorSRGB Gold
  prop ColorSRGB Goldenrod
  prop ColorSRGB Gray
  prop ColorSRGB Green
  prop ColorSRGB GreenYellow
  prop ColorSRGB Honeydew
  prop ColorSRGB HotPink
  prop ColorSRGB IndianRed
  prop ColorSRGB Indigo
  prop ColorSRGB Ivory
  prop ColorSRGB Khaki
  prop ColorSRGB Lavender
  prop ColorSRGB LavenderBlush
  prop ColorSRGB LawnGreen
  prop ColorSRGB LemonChiffon
  prop ColorSRGB LightBlue
  prop ColorSRGB LightCoral
  prop ColorSRGB LightCyan
  prop ColorSRGB LightGoldenrodYellow
  prop ColorSRGB LightGreen
  prop ColorSRGB LightGray
  prop ColorSRGB LightPink
  prop ColorSRGB LightSalmon
  prop ColorSRGB LightSeaGreen
  prop ColorSRGB LightSkyBlue
  prop ColorSRGB LightSlateGray
  prop ColorSRGB LightSteelBlue
  prop ColorSRGB LightYellow
  prop ColorSRGB Lime
  prop ColorSRGB LimeGreen
  prop ColorSRGB Linen
  prop ColorSRGB Magenta
  prop ColorSRGB Maroon
  prop ColorSRGB MediumAquamarine
  prop ColorSRGB MediumBlue
  prop ColorSRGB MediumOrchid
  prop ColorSRGB MediumPurple
  prop ColorSRGB MediumSeaGreen
  prop ColorSRGB MediumSlateBlue
  prop ColorSRGB MediumSpringGreen
  prop ColorSRGB MediumTurquoise
  prop ColorSRGB MediumVioletRed
  prop ColorSRGB MidnightBlue
  prop ColorSRGB MintCream
  prop ColorSRGB MistyRose
  prop ColorSRGB Moccasin
  prop ColorSRGB NavajoWhite
  prop ColorSRGB Navy
  prop ColorSRGB OldLace
  prop ColorSRGB Olive
  prop ColorSRGB OliveDrab
  prop ColorSRGB Orange
  prop ColorSRGB OrangeRed
  prop ColorSRGB Orchid
  prop ColorSRGB PaleGoldenrod
  prop ColorSRGB PaleGreen
  prop ColorSRGB PaleTurquoise
  prop ColorSRGB PaleVioletRed
  prop ColorSRGB PapayaWhip
  prop ColorSRGB PeachPuff
  prop ColorSRGB Peru
  prop ColorSRGB Pink
  prop ColorSRGB Plum
  prop ColorSRGB PowderBlue
  prop ColorSRGB Purple
  prop ColorSRGB Red
  prop ColorSRGB RosyBrown
  prop ColorSRGB RoyalBlue
  prop ColorSRGB SaddleBrown
  prop ColorSRGB Salmon
  prop ColorSRGB SandyBrown
  prop ColorSRGB SeaGreen
  prop ColorSRGB SeaShell
  prop ColorSRGB Sienna
  prop ColorSRGB Silver
  prop ColorSRGB SkyBlue
  prop ColorSRGB SlateBlue
  prop ColorSRGB SlateGray
  prop ColorSRGB Snow
  prop ColorSRGB SpringGreen
  prop ColorSRGB SteelBlue
  prop ColorSRGB Tan
  prop ColorSRGB Teal
  prop ColorSRGB Thistle
  prop ColorSRGB Tomato
  prop ColorSRGB Turquoise
  prop ColorSRGB Violet
  prop ColorSRGB Wheat
  prop ColorSRGB White
  prop ColorSRGB WhiteSmoke
  prop ColorSRGB Yellow
  prop ColorSRGB YellowGreen
  prop Byte X
  prop Byte Y
  prop Byte Z
  prop Byte R
  prop Byte G
  prop Byte B
  prop Byte A
  static ColorSRGB FromARGB(UInt32 packedARGB)
  UInt32 ToARGB()
  String ToString()
  Int32 GetHashCode()
  Boolean Equals(Object obj)
  Boolean Equals(ColorSRGB other)
  ColorSRGBPremultiplied ToSRGBPremultiplied()
  ColorLinear ToLinear()
  ColorLinearPremultiplied ToLinearPremultiplied()
  ColorHSV ToHSV()
  Void SetDim(Int32 i, Byte value)
  static IStreamSerializer`1 GetStreamSerializer(SerializerFormat format)
  ColorSRGB DeepClone()
  ColorSRGB DeepClone(CloningContext& context)
  AccessibleTypeInfo GetTypeInfo()

== Keen.VRage.Render.Contracts.Font (base: System.Object)
  ctor(ResourceHandle resourceHandle, IFont internalFont) 
  prop ResourceHandle ResourceHandle
  prop Int32 EmSizePx
  prop Single Ascent
  prop Single Descent
  prop Single LineGap
  prop Single UnderlinePosition
  prop Single UnderlineThickness
  prop Single StrikethroughPosition
  prop Single StrikethroughThickness
  prop IFont InternalFont
  Vector2 MeasureString(ReadOnlySpan`1 text)
  Boolean Equals(Font other)
  Boolean Equals(Object obj)
  Int32 GetHashCode()

== Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdContentRendererSessionComponent+ContentRenderCache (base: System.Object)
  ctor() 

== Keen.VRage.Library.Utils.ResourceHandle`1 (base: System.ValueType)
  ctor(Guid& guid) 
  ctor(GeneratedResourceHandle& handle) 
  ctor(ResourceHandle`1& instance, CloningContext& context) [nonpublic]
  Boolean Equals(ResourceHandle`1 other)
  Boolean Equals(Object obj)
  Int32 GetHashCode()
  String ToString()
  ResourceHandle`1 DeepClone()
  ResourceHandle`1 DeepClone(CloningContext& context)

== Keen.VRage.Core.Render.GUIAsset (base: System.Object)
  ctor() 
  prop ListReader`1 SupportedTypes
  prop Boolean Incremental
  Void RegisterAssetType(IAssetType type)

== Keen.VRage.Core.Render.TextureAsset (base: System.Object)
  ctor() 
  prop ListReader`1 SupportedTypes
  prop Boolean Incremental
  Void RegisterAssetType(IAssetType asset)

== Keen.VRage.Core.Render.FontAsset (base: System.Object)
  ctor() 
  prop ListReader`1 SupportedTypes
  prop Boolean Incremental
  Void RegisterAssetType(IAssetType type)

== Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceContext (base: System.Object)
  ctor(Int32 surfaceIndex, LcdPanelSurface definition) 
  prop Int32 SurfaceIndex
  prop LcdPanelSurface Definition
  prop LCDMaterialDefinition ScreenMaterial
  Void Dispose()
  Void ReleaseScreenMaterialHandle()
  Void SetNewScreenMaterialHandle(LcdContentRendererSessionComponent renderer, PBRMaterialDefinition materialDefinition, Single aspectRatio, LcdScreenOrientation orientation, Nullable`1 colorMetalOverride)
  Void SetSharedScreenMaterialHandle(LcdContentRendererSessionComponent renderer, PBRMaterialDefinition materialDefinition, Single aspectRatio, LcdScreenOrientation orientation)

== Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceRenderComponent (base: Keen.VRage.Core.Game.Components.GameComponent)
  ctor() 
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data

== Keen.Game2.Simulation.GameSystems.EntityNameSessionComponent (base: Keen.VRage.Core.Game.Components.SessionComponent)
  ctor() 
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Entity TryGetNamedEntity(String name)
  static Task`1 GetNamedEntitiesDebug(Session session, Object lifetime)
  static NetworkStory RequestNameEntity(Entity character, String name)
  static NetworkStory RequestEntityUnname(Session session, String name)

== Keen.Game2.Simulation.GameSystems.NamedEntity (base: System.Object)
  ctor() 
  ctor(Entity entity, String customName) 
  ctor(DEntity entity, String customName) 
  ctor(NamedEntity& instance, CloningContext& context) [nonpublic]
  prop String Name
  prop DEntity Entity
  Boolean Equals(Object obj)
  Int32 GetHashCode()
  static IStreamSerializer`1 GetStreamSerializer(SerializerFormat format)
  NamedEntity DeepClone()
  NamedEntity DeepClone(CloningContext& context)
  AccessibleTypeInfo GetTypeInfo()

== Keen.VRage.Core.VRageCore (base: Keen.VRage.Library.Utils.ManualSingleton`1[[Keen.VRage.Core.VRageCore, VRage.Core, Version=2.3.0.2798, Culture=neutral, PublicKeyToken=null]])
  ctor(CoreSettings& settings, String[] args) [nonpublic]
  prop String ApplicationName
  prop Entity Engine
  prop ContinuationQueue UpdateQueue
  prop IPlatformFactory PlatformFactory
  prop VRagePlatformCore PlatformCore
  prop AppLoop ApplicationLoop
  prop GlobalDebugSettings GlobalDebugSettings
  prop String AppDataPath
  prop Boolean SingleInstance
  prop UInt64 FrameCounter
  prop Boolean IsApplicationShown
  prop Guid Id
  prop Boolean IsDisposed
  Void Run()
  Void Update(TimeSpan timeDelta)
  Void Exit()
  Void NotifyApplicationReady()
  Void NotifyApplicationShown()
  Void Dispose()

== Keen.VRage.Core.Game.Systems.Session (base: System.Object)
  ctor() 
  prop Scene Scene
  prop HashSetReader`1 Entities
  prop Entity SessionComponents
  prop SessionExternalServices ExternalServices
  prop GameEntitySerializer EntitySerializer
  Void Dispose()
  Void Update(Boolean doEntityLifetimeUpdates)
  Void MarkEntityForClose(Entity entity, Boolean moveToStaging)
  Void MarkEntityForClose(DEntity entity, Boolean moveToStaging)
  Void AddEntityToScene(Entity entity)
  Void AddEntityBundleToScene(ReadOnlySpan`1 entities)
  Boolean IsEntityInScene(Entity entity)
  Void RemoveEntityFromScene(Entity entity)
  Void RemoveEntityFromScene(DEntity entity)
  IEnumerable`1 GetEntitiesOfType()

== Keen.VRage.Library.Utils.Singleton`1 (base: System.Object)
  ctor() [nonpublic]
  prop T Instance

== Keen.VRage.DCS.Accessors.DEntityContext (base: System.ValueType)
  ctor(Scene scene, DEntity entity) 
  ctor(Scene scene) [nonpublic]
  prop DEntityArchetype Archetype
  T Get()
  Void Set(T data)
  Boolean TryWrite(T data)
  Boolean TryGet(T& data)
  T& GetWritePtr()
  T& TryGetWritePtr()
  T& GetReadPtr()
  T& TryGetReadPtr()
  Boolean Has()
  Boolean TryRemove()
  Void SetOrRemove(Boolean condition, T data)
  Int32 GetHashCode()
  Boolean Equals(DEntityContext other)
  Boolean Equals(Object obj)
  String ToString()
  String ToDataString()

== Keen.VRage.DCS.Accessors.DEntity (base: System.ValueType)
  ctor(Int32 index, UInt32 version) [nonpublic]
  prop DEntity Empty
  prop Boolean IsEmpty
  Int32 CompareTo(DEntity other)
  Boolean Equals(Object obj)
  Boolean Equals(DEntity other)
  Int32 GetHashCode()
  String ToString()

== Keen.VRage.DCS.Components.EntityFunctions (base: System.Object)
  static Boolean IsInterface(Entity entity, Func`2 conditional)
  static Boolean IsInterface(Entity entity, TFeature& feature, Func`2 conditional)
  static TFeature AsInterface(Entity entity, Func`2 conditional)
  static TFeature Single(Entity entity, Predicate`1 conditional)
  static TFeature SingleOrDefault(Entity entity, Predicate`1 conditional)
  static TFeature First(Entity entity, Predicate`1 conditional)
  static TFeature FirstOrDefault(Entity entity, Predicate`1 conditional)
  static Scene GetSceneUnsafe(Entity entity)
  static String DebugStringify(Entity entity)

== Keen.VRage.Library.Filesystem.FileHandle (base: System.ValueType)
  ctor(RootPath root, String path) [nonpublic]
  ctor(FileHandle& instance, CloningContext& context) [nonpublic]
  prop FileSystemEntryInfo Info
  Boolean Equals(FileHandle other)
  Boolean Equals(Object obj)
  Int32 GetHashCode()
  Void AppendStableHashData(IncrementalHash hash)
  String GetExtension()
  String GetName()
  Boolean Exists()
  Stream OpenRead(FileShare share, AdvancedFileOptions options)
  String ToString()
  Task`1 ReadAllBytesAsync(Int64 fileOffset, Memory`1 memory)
  Int32 ReadAllBytes(Int64 fileOffset, Span`1 memory)
  String GetAbsolutePath()
  FileHandle DeepClone()
  FileHandle DeepClone(CloningContext& context)

== Keen.VRage.Library.Filesystem.RootPath (base: System.Enum)

== Keen.VRage.Library.Utils.ResourceHandle (base: System.ValueType)
  ctor(GeneratedResourceHandle& handle) 
  ctor(Guid& guid) 
  ctor(ResourceHandle& instance, CloningContext& context) [nonpublic]
  Boolean Equals(ResourceHandle other)
  Boolean Equals(Object obj)
  Int32 GetHashCode()
  String ToString()
  static ResourceHandle GetOrRegister(FileHandle file, Boolean logWarningOnRegister)
  static ResourceHandle NewGuidResourceHandle()
  static Guid NewGuid()
  ResourceHandle DeepClone()
  ResourceHandle DeepClone(CloningContext& context)

== Keen.VRage.Library.Filesystem.ContentCache.ContentCache (base: System.Object)
  ctor() 
  ListReader`1 GetAssets()
  Void LoadContentCacheData(ContentBlobData contentCacheData, IFileReader originatingFileSystem, String mountPath)
  Boolean TryGetData(ResourceHandle resourceHandle, T& data)
  T GetData(ResourceHandle resourceHandle)
  Void CollectRecordedAssetBlobs(ListDictionary`2 target)
  Boolean TryTranslateResourceHandle(ResourceHandle resourceHandle, FileHandle& fileHandle)
  Boolean TryGetProjectIdentifier(ResourceHandle handle, String& identifier)
  Boolean TryTranslateFileHandle(FileHandle fileHandle, ResourceHandle& resourceHandle)
  ResourceHandle RegisterFile(FileHandle fileHandle, String projectMapping)
  Void SetMapping(ResourceHandle resourceHandle, FileHandle fileHandle)
  Void Unregister(ResourceHandle resourceHandle)
  Task OnBeforeChanged(ResourceHandle handle)
  Task OnAfterChanged(ResourceHandle handle)
  static ContentBlobData DeserializeCC(Stream data, String mountedPath)

== Keen.VRage.Library.Filesystem.IFileSystem (base: )
  prop Boolean IsReadOnly
  Stream Open(String file, FileMode mode, FileAccess access, FileShare share, AdvancedFileOptions options)
  Void CreateDirectory(String path)
  Void CopyFile(String source, String destination, Boolean overwrite)
  Void MoveFile(String source, String destination, Boolean overwrite)
  Void MoveDirectory(String source, String destination)
  Void DeleteFile(String path)
  Void DeleteDirectory(String path)
  Void SetAttributes(String path, PathType type, FileSystemEntryFlags flags)

== Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdContentRendererSessionComponent (base: Keen.VRage.Core.Game.Components.SessionComponent)
  ctor() 
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Void Render(IDrawBatch batch, LcdPanelSurfaceContext surface)

== Keen.Game2.Client.GameSystems.CameraSystems.CameraSystemComponent (base: Keen.VRage.Core.Game.Components.GameComponent)
  ctor() 
  prop Entity ObservedEntity
  prop Entity RenderCameraEntity
  prop Entity ActiveCameraController
  prop Nullable`1 ActiveCameraModeIndex
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Boolean CanEntityBeObserved(Entity entity)
  Void ResetCameraModeForEntity(Entity targetEntity)
  Boolean TrySetCameraModeForEntity(Entity targetEntity, KeyDefinition cameraModeKey)
  Void EnableCameraInput(Boolean enable)
  Boolean TrySetCameraModeForEntity(Entity targetEntity, Int32 cameraModeIndex, Object cameraData)
  Void RequestCameraChange(Entity entityToObserve)
  Task WaitForCameraChange(Entity entityToObserve)
  Boolean RequestSpecificCamera(KeyDefinition key)

== Keen.Game2.Client.GameSystems.CameraSystems.ICameraSystem (base: )
  prop Entity ObservedEntity
  prop Entity RenderCameraEntity
  prop Entity ActiveCameraController
  prop Nullable`1 ActiveCameraModeIndex
  Boolean CanEntityBeObserved(Entity entity)
  Void RequestCameraChange(Entity entityToObserve)
  Task WaitForCameraChange(Entity entityToObserve)
  Boolean RequestSpecificCamera(KeyDefinition key)
  Void EnableCameraInput(Boolean enable)
  Void ResetCameraModeForEntity(Entity targetEntity)
  Boolean TrySetCameraModeForEntity(Entity targetEntity, KeyDefinition cameraModeKey)
  Boolean TrySetCameraModeForEntity(Entity targetEntity, Int32 cameraModeIndex, Object cameraData)

== Keen.Game2.Client.GameSystems.CameraSystems.CameraComponent (base: Keen.VRage.Core.Game.Components.GameComponent)
  ctor() 
  prop Single AspectRatio
  prop Single FieldOfView
  prop Vector2I Resolution
  prop Single NearPlane
  prop MatrixD ViewProjectionMatrix
  prop Matrix ProjectionMatrix
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  Void SetCustomFOV(Single fov)
  Void ResetCustomFOV()
  Single ToScreenSpace(Single percentOffset)
  Vector3 GetNearPlaneHalfExtents()
  Vector3 WorldToProjected(Vector3D& worldPosition)
  Vector2 ProjectedToScreenPointNormalized(Vector3 projectedPosition)
  Vector2 WorldToScreenPoint(Vector3D& worldPosition)
  Vector3D ScreenToWorldPoint(Vector2& screenPosition, Single& ndcZ)
  Vector2 NormalizedScreenToScreenPoint(Vector2 normalizedScreenPosition)
  Void SetNextTransitionNonSmooth()
  Void UpdateRenderSettings()
  Void SetTransformOverride(Nullable`1 overrideTransform)

== Keen.Game2.Client.GameSystems.CameraSystems.CameraData (base: System.ValueType)
  Entity GetSpectatedEntity(Scene scene)

== Keen.Game2.Simulation.Utils.PhysicsExtensions (base: System.Object)
  static CubeGridComponent CastRayForClosestGrid(IPhysics physics, Scene scene, WorldTransform rayOriginTransform, Vector3 rayDirectionLocal, Single rayDistance)
  static CubeGridComponent CastRayForClosestGrid(IPhysics physics, Scene scene, RayCastArgs ray)

== Keen.VRage.Core.Game.GameSystems.Queries.RayCastArgs (base: System.ValueType)
  ctor(Vector3D& position, Vector3& direction) 
  Void Dispose()
  static RayCastArgs CreateFromTo(Vector3D from, Vector3D to)
  static RayCastArgs CreateFromDirection(Vector3D from, Vector3 direction, Single length)

== Keen.VRage.Core.WorldTransform (base: System.ValueType)
  ctor(Vector3D position, Quaternion orientation) 
  ctor(Vector3D position) 
  ctor(Quaternion orientation) 
  ctor(WorldTransform& instance, CloningContext& context) [nonpublic]
  String ToString()
  Boolean Equals(WorldTransform& other)
  Boolean Equals(Object obj)
  Int32 GetHashCode()
  static Vector3D Transform(Vector3D& position, WorldTransform& worldTransform)
  static Vector3D TransformInv(Vector3D position, WorldTransform worldTransform)
  static Vector3 TransformDirection(Vector3 direction, WorldTransform worldTransform)
  static Vector3D TransformDirection(Vector3D direction, WorldTransform worldTransform)
  static Vector3 TransformDirectionInv(Vector3 direction, WorldTransform worldTransform)
  static Vector3D TransformDirectionInv(Vector3D direction, WorldTransform worldTransform)
  static WorldTransform GetRelativeTransform(WorldTransform transform, WorldTransform parentTransform)
  static WorldTransform Invert(WorldTransform worldTransform)
  Boolean IsValid()
  Boolean IsValidAndRotationIsNormalized(Single epsilon)
  Void AssertIsValid()
  static WorldTransform Lerp(WorldTransform& transform1, WorldTransform& transform2, Single amount)
  static WorldTransform Slerp(WorldTransform& transform1, WorldTransform& transform2, Single amount)
  static BoundingBoxD Transform(BoundingBoxD& originalBox, WorldTransform& worldTransform)
  static OrientedBoundingBoxD TransformOriented(BoundingBoxD& originalBox, WorldTransform& worldTransform)
  static OrientedBoundingBoxD Transform(OrientedBoundingBoxD& originalBox, WorldTransform& worldTransform)
  static BoundingSphereD Transform(BoundingSphereD& originalSphere, WorldTransform& worldTransform)
  static BoundingBoxD TransformInv(BoundingBoxD& originalBox, WorldTransform& worldTransform)
  static OrientedBoundingBoxD TransformInv(OrientedBoundingBoxD& originalBox, WorldTransform& worldTransform)
  static RayD Transform(RayD& ray, WorldTransform& worldTransform)
  static RayD TransformInv(RayD& ray, WorldTransform& worldTransform)
  static LineD Transform(LineD& line, WorldTransform& worldTransform)
  static LineD TransformInv(LineD& line, WorldTransform& worldTransform)
  static IStreamSerializer`1 GetStreamSerializer(SerializerFormat format)
  WorldTransform DeepClone()
  WorldTransform DeepClone(CloningContext& context)
  AccessibleTypeInfo GetTypeInfo()

== Keen.VRage.Render.Data.GradientFillData (base: System.ValueType)
  prop ColorSRGB SecondaryColor
  prop Vector2 StartPoint
  prop Vector2 Endpoint
  prop GradientType Type
  static GradientFillData CreateLinearGradient(Vector2 startPoint, Vector2 endPoint, ColorSRGB secondaryColor)
  static GradientFillData CreateRadialGradient(Vector2 centerPoint, Single radius, ColorSRGB secondaryColor)

== Keen.VRage.Render.Data.GradientType (base: System.Enum)

== Keen.VRage.Render.Contracts.OffscreenRenderTarget (base: System.ValueType)
  prop RenderId Id
  prop Boolean IsValid
  prop ResourceHandle`1 TextureHandle
  Void Dispose()
  Void TakeScreenshotToMemory(Boolean waitUntilFullyLoaded)

== Keen.VRage.Render.Contracts.UISystem (base: System.Object)
  ctor() 
  Font GetFont(ResourceHandle`1 resourceHandle)
  Void PreloadTexture(ResourceHandle handle)
  Void SetMainViewportScale(Single scaleFactor)
  ImmediateDrawBatch CreateImmediateMainViewBatch(Int32 sortLayer, String debugName)
  PersistentDrawBatch CreatePersistentMainViewBatch(Int32 sortLayer, IDrawBatch previousBatch, Boolean deletePrevious)
  ImmediateDrawBatch CreateImmediateBatchFor(Nullable`1 renderTarget, Int32 sortLayer, String debugName)
  PersistentDrawBatch CreatePersistentBatchFor(Nullable`1 renderTarget, Int32 sortLayer, IDrawBatch previousBatch, Boolean deletePrevious)
  Vector2I GetTextureSize(ResourceHandle handle)

== Keen.VRage.Render.Contracts.ImmediateDrawBatch (base: System.Object)
  ctor() [nonpublic]
  prop RenderDrawCommandBuffer CommandBuffer
  Void Dispose()
  Void Submit()
  Void MoveToEnd()
  Void DrawString(Font font, Vector2 screenCoord, ColorSRGB colorMask, String text, Single screenScale, Boolean ignoreBounds, Nullable`1 maxTextWidth, Single rotation)
  Void DrawSubstring(Font font, Vector2 screenCoord, ColorSRGB colorMask, ReadOnlySpan`1 text, Single screenScale, Boolean ignoreBounds, Nullable`1 maxTextWidth)
  Void DrawStringAligned(Font font, Vector2 screenCoord, ColorSRGB colorMask, String text, Single fontScale, Boolean ignoreBounds, Nullable`1 maxTextWidth, TextAlignmentEnum align)
  Void DrawStringAligned3D(Font font, Vector3 textCoord, ColorSRGB colorMask, String text, Single fontScale, Boolean ignoreBounds, Nullable`1 rootEntity, Nullable`1 maxTextWidth, TextAlignmentEnum align)
  Void DrawLine(Vector2 from, Vector2 to, ColorSRGB color, Single width, DashingTypeEnum dashingType, Single dashingScale, Boolean ignoreBounds)
  Void DrawPath(ReadOnlySpan`1 splines, ColorSRGB strokeColor, Single strokeWidth, Boolean ignoreBounds)
  Void DrawPathExt(ReadOnlySpan`1 splines, ColorSRGB strokeColor, Single strokeWidth, ReadOnlySpan`1 dashesAndGaps, Single dashOffset, LineCapEnum lineCap, LineJoinEnum lineJoin, Single miterLimit, Boolean ignoreBounds)
  Void DrawFill(ReadOnlySpan`1 splines, ColorSRGB primaryColor, Nullable`1 gradientFill, Boolean ignoreBounds)
  Void DrawImage(ResourceHandle texture, BoundingBox2& destination, ColorSRGB color, Boolean ignoreBounds, Nullable`1 maskTexture, Nullable`1& sourceRectangle)
  Void DrawImageExt(ResourceHandle texture, BoundingBox2& destination, ColorSRGB color, Vector2 rotationPivot, Single rotation, Boolean ignoreBounds, Single rotationSpeed, Nullable`1 maskTexture, Nullable`1& sourceRectangle)
  Void DrawVideoExt(RenderId videoPlayerRenderId, BoundingBox2I& destination)
  Void ScissorPush(BoundingBox2I screenRectangle)
  Void ScissorPop()

== Keen.VRage.Render.Contracts.RenderContracts (base: System.Object)
  ctor() 
  RenderSystem GetRenderSystem()
  DecalSystem GetDecalSystem()
  ParticleSystem GetParticleSystem()
  FloraSystem GetFloraSystem()
  MeshEffectSystem GetMeshEffectSystem()
  MaterialSystem GetMaterialSystem()
  UISystem GetUISystem()
  WaterSystem GetWaterSystem()
  MainRenderTarget GetMainTarget()
  RenderSettings GetSettings()
  RootEntity CreateRootEntity(String debugName, WorldTransform& worldTransform, Boolean autoActivate)
  VideoPlayerEntity CreateVideoPlayerEntity(ResourceHandle`1 videoHandle)
  DecalEntity CreateDecalEntity(String debugName, RelativeTransform localTransform, DecalMaterialDefinition decalMaterial, DecalEntityParentMethod parentMethod, DecalCreationParameters parameters)
  PlanetEnvironmentEntity CreatePlanetEnvironmentEntity(String debugName, AtmosphereDefinition atmosphereDefinition, Single atmosphereRadius, Single radiusWithMaxHills, CloudDefinition cloudDefinition, SpherizationData sphereData, SpherizationData atmosphereSpherizationData, SpherizationData skyboxSpherizationData, Single spherizeRadius, Vector3D planetCenter, PlanetOverlayDefinition planetOverlayDefinition, ResourceHandle`1[] preloadItems)
  WeatherModifierEntity CreateWeatherModifierEntity(String debugName, RenderId planetEnvEntityId, WeatherModifierParameters& parameters)
  FloraSectorEntity CreateFloraSector(String debugName, RootEntity rootEntity, Buffer`1 floraInstances, WorldTransform planetTransform)
  GrassEntity CreateGrassEntityForVoxel(String debugName, RootEntity rootEntity, ResourceHandle modelResourceHandle, RelativeTransform localTransform, Buffer`1 grassMaterialsUsed, Int32 lod, Boolean showImmediately)
  Void UpdateGrassMaterialsArray(ImmutableArray`1 grassMaterialDefinition)
  Void SetGrassWindProjection(Nullable`1 projectionInfo)
  GravityProbeRenderEntity CreateGravityProbeRenderEntity(String debugName)
  PointLightEntity CreatePointLightEntity(String debugName, ColorLinear lightIntensityRGB, RelativeTransform localLightTransform, Nullable`1 rootEntity, Nullable`1 localFlareTransform, FlareDefinition flareDefinition, Single glossinessMultiplier, Single falloffMultiplier, Boolean castShadows)
  SpotLightEntity CreateSpotLightEntity(String debugName, ColorLinear lightIntensityRGB, Single outerConeAngle, RelativeTransform localLightTransform, Nullable`1 rootEntity, Nullable`1 localFlareTransform, ResourceHandle cookieTexture, FlareDefinition flareDefinition, Boolean tryForceShadowMapAlwaysAllocated, Single falloffMultiplier, Single outerConeStartRadius)
  CapsuleLightEntity CreateCapsuleLightEntity(String debugName, ColorLinear lightIntensityRGB, Single lineLength, RelativeTransform localTransform, Single radius, Nullable`1 rootEntity)
  AreaLightEntity CreateAreaLightEntity(String debugName, ColorLinear lightIntensityRGB, Vector2 dimensions, Single barnAngle, Single barnLength, RelativeTransform localTransform, Nullable`1 rootEntity, ResourceHandle imageTexture)
  ModelEntity CreateModelEntity(String debugName, ResourceHandle model, RelativeTransform localTransform, Nullable`1 rootEntity, RenderFlags flags, EntityType type, Nullable`1 planet)
  InstancedModelEntity CreateInstancedModelEntity(String debugName, ResourceHandle model, RelativeTransform localTransform, GeneratedResourceHandle instanceData, BoundingBox boundingBox, Nullable`1 rootEntity, RenderFlags flags, Boolean implicitLifetime)
  ParticleEffectEntity CreateParticleEffectEntity(String debugName, WorldTransform localTransform, ParticleEffectDefinition particleEffectDefinition, ParticleEffectUserParameters userParams, RootEntity rootEntity, EntityType entityType)
  ParticleEffectEntity CreateEmptyPooledParticleEffectEntity(String debugName)
  Void DestroyParticleEffect(RenderId id, Boolean tryDestroy)
  ParticleEffectEntity CreatePreviewParticleEffectEntity(String debugName, WorldTransform localTransform, ParticleEffectDefinition particleEffectDefinition, ParticleEffectUserParameters userParams, RootEntity rootEntity)
  ModelParticleEffectEntity CreateModelParticleEffectEntity(String debugName, RelativeTransform localToModelTransform, ParticleEffectDefinition particleEffectDefinition, ParticleEffectUserParameters userParams, DEntity modelEntity, Nullable`1 boneIndex)
  Void SetParticleKillZone(BoundingBoxD boundingBox)
  RuntimeModel CreateRuntimeModel(IRuntimeMeshData meshData, RenderRuntimeDataType runtimeDataType, Boolean immediateUpload, Boolean prepareBLAS)
  RuntimeModel CreateRuntimeModel(Buffer`1 lodData, RenderRuntimeDataType runtimeDataType, Boolean immediateUpload, Boolean prepareBLAS)
  RuntimeBuffer CreateUnmanagedRuntimeBuffer(String debugName, ReadOnlySpan`1 bufferData, RenderRuntimeDataType runtimeDataType)
  RuntimeBuffer CreateRuntimeBuffer(String debugName, ReadOnlySpan`1 bufferData, RenderRuntimeDataType runtimeDataType)
  RuntimeTexture3D CreateRuntimeTexture3D(IRuntimeTextureData textureData)
  OffscreenRenderTarget CreateOffscreenTarget(String name, Vector2I resolution)
  ModelResourcePin CreateModelResourcePin(Buffer`1 handles)
  ResourcePin CreateTextureResourcePin(Buffer`1 handles, ResourcePinType type, TextureResourcePinDimension dimension, String debugTag)
  ResourcePin CreateAssetResourcePin(Buffer`1 handles, String debugTag)
  Void UpdateCloudDefinitions(ImmutableArray`1 cloudDefinitions)
  Void UpdateAtmosphereDefinitions(ImmutableArray`1 atmosphereDefinitions)
  Void UpdateFlareDefinitions(ImmutableArray`1 flareDefinitions)
  Void UpdateParticleEffectDefinitions(ImmutableArray`1 particleEffectDefinitions)
  Void UpdateParticleEmitterDefinitions(ImmutableArray`1 particleEmitterDefinitions)
  Void UpdateWindAnimationDefinitions(ImmutableArray`1 windAnimationDefinitions)
  Void UpdateModelGroups(PooledMap`2 modelMap)
  Void OverrideTextureStreamingBehaviour(Nullable`1 overriddenTextureStreamingBehaviour)
  Void DisableTextureCachingForSingleFrame()
  Void SetFloraSystemTaskDeadline(Nullable`1 taskDeadline)
  Task`1 CaptureVideoMemorySnapshot()
  Task`1 CollectTextureStreamingSnapshot()
  Void SetTextureStreamingOverride(AssetSnapshotBase textureStreamingSnapshot)
  Void ResetTextureStreamingOverride()
  WaterRenderEntity CreateWaterRenderEntity(String debugName, RootEntity rootEntity, RelativeTransform localTransform)

== Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdRenderTargetPoolSessionComponent (base: Keen.VRage.Core.Game.Components.SessionComponent)
  ctor() 
  prop Entity Entity
  prop DEntity DEntity
  prop DEntityContext Data
  OffscreenRenderTarget Borrow(String debugName, Vector2I resolution)
  Void Return(OffscreenRenderTarget rt, Vector2I resolution)

== Keen.VRage.Physics.CollisionPreset (base: System.ValueType)
  ctor(CollisionPresetType type) 
  prop CollisionPreset Default
  prop CollisionPreset Closest
  prop CollisionPreset Any
  prop CollisionPreset Bodies
  AccessibleTypeInfo GetTypeInfo()

== Keen.VRage.Physics.Queries.SweepQueryHit (base: System.ValueType)
  Boolean IsInsideHit()
  Boolean IsOutsideHit()
  Boolean IsFrontSideHit()
  Boolean IsBackSideHit()

== Keen.VRage.Physics.Queries.NearestQueryHit (base: System.ValueType)

== MISSING Keen.VRage.Library.Collections.BufferReference`1
== MISSING Keen.VRage.Library.Collections.Buffer`1
== Keen.VRage.Core.Game.Systems.Session (base: System.Object)
  ctor() 
  prop Scene Scene
  prop HashSetReader`1 Entities
  prop Entity SessionComponents
  prop SessionExternalServices ExternalServices
  prop GameEntitySerializer EntitySerializer
  Void Dispose()
  Void Update(Boolean doEntityLifetimeUpdates)
  Void MarkEntityForClose(Entity entity, Boolean moveToStaging)
  Void MarkEntityForClose(DEntity entity, Boolean moveToStaging)
  Void AddEntityToScene(Entity entity)
  Void AddEntityBundleToScene(ReadOnlySpan`1 entities)
  Boolean IsEntityInScene(Entity entity)
  Void RemoveEntityFromScene(Entity entity)
  Void RemoveEntityFromScene(DEntity entity)
  IEnumerable`1 GetEntitiesOfType()

== All types in Keen.VRage.Core.Plugins namespace:
  pub interface IPlugin
  pub class PluginHost
  int class <>c
  int class <>c__DisplayClass11_0
