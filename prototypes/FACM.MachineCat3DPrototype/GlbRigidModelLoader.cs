using System.Buffers.Binary;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace FACM.MachineCat3DPrototype;

internal sealed record RigidMeshPart(string Name, MeshGeometry3D Geometry, Point3D Center, Rect3D Bounds);

internal sealed class RigidModel
{
    public RigidModel(IReadOnlyList<RigidMeshPart> parts, Rect3D bounds)
    {
        Parts = parts;
        Bounds = bounds;
    }

    public IReadOnlyList<RigidMeshPart> Parts { get; }
    public Rect3D Bounds { get; }
}

/// <summary>
/// Deliberately small GLB 2.0 loader for the current Gate 1 asset shape:
/// triangle primitives, FLOAT VEC3 positions/normals, integer indices and node matrices.
/// It is not intended to become a general glTF engine.
/// </summary>
internal static class GlbRigidModelLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;

    public static RigidModel Load(string path) => Load(File.ReadAllBytes(path));

    internal static RigidModel Load(byte[] data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)) != GlbMagic)
            throw new InvalidDataException("Not a GLB file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) != 2)
            throw new InvalidDataException("Only glTF/GLB 2.0 is supported.");

        var declaredLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
        if (declaredLength > data.Length) throw new InvalidDataException("Invalid GLB length.");

        ReadOnlyMemory<byte> jsonBytes = default;
        ReadOnlyMemory<byte> binaryBytes = default;
        for (var offset = 12; offset + 8 <= declaredLength;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            offset += 8;
            if (length < 0 || offset + length > declaredLength) throw new InvalidDataException("Invalid GLB chunk.");
            if (type == JsonChunk) jsonBytes = data.AsMemory(offset, length);
            if (type == BinChunk) binaryBytes = data.AsMemory(offset, length);
            offset += length;
        }
        if (jsonBytes.IsEmpty || binaryBytes.IsEmpty) throw new InvalidDataException("GLB JSON/BIN chunk missing.");

        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;
        var nodes = root.GetProperty("nodes");
        var meshes = root.GetProperty("meshes");
        var accessors = root.GetProperty("accessors");
        var views = root.GetProperty("bufferViews");
        var scenes = root.GetProperty("scenes");
        var sceneIndex = root.TryGetProperty("scene", out var sceneProp) ? sceneProp.GetInt32() : 0;

        var parts = new List<RigidMeshPart>();
        foreach (var item in scenes[sceneIndex].GetProperty("nodes").EnumerateArray())
            Visit(item.GetInt32(), Matrix4.Identity, nodes, meshes, accessors, views, binaryBytes, parts);
        if (parts.Count == 0) throw new InvalidDataException("GLB has no triangle meshes.");

        var bounds = Rect3D.Empty;
        foreach (var part in parts) Union(ref bounds, part.Bounds);
        return new RigidModel(parts, bounds);
    }

    private static void Visit(
        int nodeIndex, Matrix4 parent, JsonElement nodes, JsonElement meshes,
        JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary,
        List<RigidMeshPart> parts)
    {
        var node = nodes[nodeIndex];
        var world = Matrix4.Multiply(parent, ReadMatrix(node));

        if (node.TryGetProperty("mesh", out var meshIndex))
        {
            var mesh = meshes[meshIndex.GetInt32()];
            var baseName = node.TryGetProperty("name", out var name)
                ? name.GetString() ?? $"mesh-{meshIndex.GetInt32()}"
                : $"mesh-{meshIndex.GetInt32()}";
            var primitiveNo = 0;
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
                    throw new InvalidDataException($"{baseName}: only TRIANGLES is supported.");
                var attrs = primitive.GetProperty("attributes");
                var positions = ReadPoints(attrs.GetProperty("POSITION").GetInt32(), accessors, views, binary);
                var normals = attrs.TryGetProperty("NORMAL", out var normalIndex)
                    ? ReadVectors(normalIndex.GetInt32(), accessors, views, binary)
                    : Array.Empty<Vector3D>();
                var indices = primitive.TryGetProperty("indices", out var indicesIndex)
                    ? ReadIndices(indicesIndex.GetInt32(), accessors, views, binary)
                    : Enumerable.Range(0, positions.Length).ToArray();

                var geometry = new MeshGeometry3D();
                var bounds = Rect3D.Empty;
                foreach (var position in positions)
                {
                    var p = world.Point(position);
                    geometry.Positions.Add(p);
                    Include(ref bounds, p);
                }
                if (normals.Length == positions.Length)
                {
                    foreach (var normal in normals)
                    {
                        var n = world.Vector(normal);
                        if (n.LengthSquared > 0.0000001) n.Normalize();
                        geometry.Normals.Add(n);
                    }
                }
                foreach (var index in indices) geometry.TriangleIndices.Add(index);
                geometry.Freeze();

                var partName = primitiveNo == 0 ? baseName : $"{baseName}#{primitiveNo}";
                var center = new Point3D(
                    bounds.X + bounds.SizeX / 2d,
                    bounds.Y + bounds.SizeY / 2d,
                    bounds.Z + bounds.SizeZ / 2d);
                parts.Add(new RigidMeshPart(partName, geometry, center, bounds));
                primitiveNo++;
            }
        }

        if (node.TryGetProperty("children", out var children))
            foreach (var child in children.EnumerateArray())
                Visit(child.GetInt32(), world, nodes, meshes, accessors, views, binary, parts);
    }

    private static Point3D[] ReadPoints(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var raw = ReadFloatVec3(accessorIndex, accessors, views, binary);
        var result = new Point3D[raw.Length / 3];
        for (var i = 0; i < result.Length; i++) result[i] = new Point3D(raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
        return result;
    }

    private static Vector3D[] ReadVectors(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var raw = ReadFloatVec3(accessorIndex, accessors, views, binary);
        var result = new Vector3D[raw.Length / 3];
        for (var i = 0; i < result.Length; i++) result[i] = new Vector3D(raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
        return result;
    }

    private static double[] ReadFloatVec3(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var accessor = accessors[accessorIndex];
        if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.GetProperty("type").GetString() != "VEC3")
            throw new InvalidDataException("Expected FLOAT VEC3.");
        var count = accessor.GetProperty("count").GetInt32();
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        var start = OptionalInt(view, "byteOffset") + OptionalInt(accessor, "byteOffset");
        var stride = view.TryGetProperty("byteStride", out var strideProp) ? strideProp.GetInt32() : 12;
        var result = new double[count * 3];
        var span = binary.Span;
        for (var i = 0; i < count; i++)
        {
            var p = start + i * stride;
            Ensure(span.Length, p, 12);
            result[i * 3] = ReadFloat(span, p);
            result[i * 3 + 1] = ReadFloat(span, p + 4);
            result[i * 3 + 2] = ReadFloat(span, p + 8);
        }
        return result;
    }

    private static int[] ReadIndices(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var accessor = accessors[accessorIndex];
        var component = accessor.GetProperty("componentType").GetInt32();
        var bytes = component switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new InvalidDataException("Unsupported index type.") };
        var count = accessor.GetProperty("count").GetInt32();
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        var start = OptionalInt(view, "byteOffset") + OptionalInt(accessor, "byteOffset");
        var stride = view.TryGetProperty("byteStride", out var strideProp) ? strideProp.GetInt32() : bytes;
        var result = new int[count];
        var span = binary.Span;
        for (var i = 0; i < count; i++)
        {
            var p = start + i * stride;
            Ensure(span.Length, p, bytes);
            result[i] = component switch
            {
                5121 => span[p],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2)),
                5125 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4))),
                _ => 0
            };
        }
        return result;
    }

    private static Matrix4 ReadMatrix(JsonElement node)
    {
        if (!node.TryGetProperty("matrix", out var matrix)) return Matrix4.Identity;
        var values = matrix.EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (values.Length != 16) throw new InvalidDataException("Node matrix must contain 16 values.");
        return new Matrix4(values);
    }

    private static int OptionalInt(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
    private static float ReadFloat(ReadOnlySpan<byte> span, int offset) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4)));
    private static void Ensure(int length, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > length - count) throw new InvalidDataException("Accessor is outside BIN chunk.");
    }
    private static void Include(ref Rect3D bounds, Point3D p)
    {
        if (bounds.IsEmpty) bounds = new Rect3D(p, new Size3D(0, 0, 0)); else bounds.Union(p);
    }
    private static void Union(ref Rect3D bounds, Rect3D other)
    {
        if (other.IsEmpty) return;
        if (bounds.IsEmpty) bounds = other; else bounds.Union(other);
    }

    private readonly struct Matrix4
    {
        private readonly double[] _m;
        public Matrix4(double[] m) => _m = m;
        public static Matrix4 Identity => new(new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 });
        public static Matrix4 Multiply(Matrix4 a, Matrix4 b)
        {
            var r = new double[16];
            for (var c = 0; c < 4; c++) for (var row = 0; row < 4; row++)
                for (var k = 0; k < 4; k++) r[c * 4 + row] += a._m[k * 4 + row] * b._m[c * 4 + k];
            return new Matrix4(r);
        }
        public Point3D Point(Point3D p) => new(
            _m[0] * p.X + _m[4] * p.Y + _m[8] * p.Z + _m[12],
            _m[1] * p.X + _m[5] * p.Y + _m[9] * p.Z + _m[13],
            _m[2] * p.X + _m[6] * p.Y + _m[10] * p.Z + _m[14]);
        public Vector3D Vector(Vector3D v) => new(
            _m[0] * v.X + _m[4] * v.Y + _m[8] * v.Z,
            _m[1] * v.X + _m[5] * v.Y + _m[9] * v.Z,
            _m[2] * v.X + _m[6] * v.Y + _m[10] * v.Z);
    }
}
