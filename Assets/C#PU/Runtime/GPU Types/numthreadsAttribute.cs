using System;

[AttributeUsage(System.AttributeTargets.Method)]
public sealed class numthreadsAttribute : Attribute
{
    public uint numThreadsX;

    public uint numThreadsY;

    public uint numThreadsZ;

    public numthreadsAttribute(uint numThreadsX, uint numThreadsY, uint numThreadsZ)
    {
        this.numThreadsX = numThreadsX;
        this.numThreadsY = numThreadsY;
        this.numThreadsZ = numThreadsZ;
    }
}
