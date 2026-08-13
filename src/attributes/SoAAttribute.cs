namespace TRMesh;

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class SoAAttribute : Attribute
{
    public SoAAttribute() { }

    public SoAAttribute(Type onto)
    {
        Onto = onto;
    }

    public Type? Onto { get; }
}
