using TRMesh;

namespace tr_mesh.Tests.SourceHintCollisionA
{
    [SoA]
    public struct DuplicateEntity
    {
        public int Value;
    }
}

namespace tr_mesh.Tests.SourceHintCollisionB
{
    [SoA]
    public struct DuplicateEntity
    {
        public int Value;
    }
}

namespace tr_mesh.Tests
{
    internal struct InternalFieldPayload
    {
        public int Value;
    }

    [SoA]
    public struct InternalFieldEntity
    {
        internal InternalFieldPayload Payload;
        public int Visible;
    }

    [SoA]
    public struct IdentifierEntity
    {
        public int Foo;
        public int foo;
        public int @event;
        public int _soa;
        public int _slot;
        public int soa;
        public int slot;
        public int View;
    }

    [SoA(typeof(TargetMesh))]
    public struct TopologyInfo
    {
        public int Vertex;
        public int Next;
    }

    public partial class TargetMesh { }

    [SoA(typeof(TargetSpatialMesh))]
    public struct SpatialInfo
    {
        public float Position;
        public float UV;
    }

    public partial class TargetSpatialMesh : TargetMesh { }

    [SoA(typeof(MultiComponentTarget))]
    public struct AlphaInfo
    {
        public int Alpha;
    }

    [SoA(typeof(MultiComponentTarget))]
    public struct BetaInfo
    {
        public float Beta;
    }

    public partial class MultiComponentTarget { }
}
