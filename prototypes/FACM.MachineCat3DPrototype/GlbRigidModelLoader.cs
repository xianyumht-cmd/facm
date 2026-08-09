using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
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

internal static class GlbRigidModelLoader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;

    public static RigidModel Load(string path) => Load(File.ReadAllBytes(path));

    internal static RigidModel Load(byte[] data)
    {
        if (data.Length < 20)
            throw new InvalidDataException("GLB is too small.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)) != GlbMagic)
            throw new InvalidDataException("Not a glTF binary (GLB) file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) != 2)
            throw new InvalidDataException("Only GLB/glTF 2.0 is supported.");

        var declaredLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
        if (declaredLength > data.Length)
            throw new InvalidDataException("GLB length is invalid.");

        ReadOnlyMemory<byte> jsonBytes = default;
        ReadOnlyMemory<byte> binaryBytes = default;
        var offset = 12;
        while (offset + 8 <= declaredLength)
        {
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            offset += 8;
            if (chunkLength < 0 || offset + chunkLength > declaredLength)
                throw new InvalidDataException("GLB chunk length is invalid.");

            if (chunkType == JsonChunk)
                jsonBytes = data.AsMemory(offset, chunkLength);
            else if (chunkType == BinChunk)
                binaryBytes = data.AsMemory(offset, chunkLength);
            offset += chunkLength;
        }

        if (jsonBytes.IsEmpty || binaryBytes.IsEmpty)
            throw new InvalidDataException("GLB must contain JSON and BIN chunks.");

        using var document = JsonDocument.Parse(jsonBytes);
        var root = document.RootElement;
        var nodes = root.GetProperty("nodes");
        var meshes = root.GetProperty("meshes");
        var accessors = root.GetProperty("accessors");
        var bufferViews = root.GetProperty("bufferViews");
        var scenes = root.GetProperty("scenes");
        var sceneIndex = root.TryGetProperty("scene", out var selectedScene) ? selectedScene.GetInt32() : 0;
        var scene = scenes[sceneIndex];

        var parts = new List<RigidMeshPart>();
        foreach (var nodeIndexValue in scene.GetProperty("nodes").EnumerateArray())
            VisitNode(nodeIndexValue.GetInt32(), Matrix4.Identity, nodes, meshes, accessors, bufferViews, binaryBytes, parts);

        if (parts.Count == 0)
            throw new InvalidDataException("GLB contains no triangle mesh parts.");

        var overall = EmptyBounds();
        foreach (var part in parts)
            Union(ref overall, part.Bounds);
        return new RigidModel(parts, overall);
    }

    private static void VisitNode(
        int nodeIndex,
        Matrix4 parentWorld,
        JsonElement nodes,
        JsonElement meshes,
        JsonElement accessors,
        JsonElement bufferViews,
        ReadOnlyMemory<byte> binary,
        List<RigidMeshPart> parts)
    {
        var node = nodes[nodeIndex];
        var local = ReadNodeMatrix(node);
        var world = Matrix4.Multiply(parentWorld, local);

        if (node.TryGetProperty("mesh", out var meshProperty))
        {
            var mesh = meshes[meshProperty.GetInt32()];
            var baseName = node.TryGetProperty("name", out var nameProperty)
                ? nameProperty.GetString() ?? $"mesh-{meshProperty.GetInt32()}"
                : $"mesh-{meshProperty.GetInt32()}";

            var primitiveNumber = 0;
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
                    throw new InvalidDataException($"{baseName}: only TRIANGLES primitives are supported.");

                var attributes = primitive.GetProperty("attributes");
                if (!attributes.TryGetProperty("POSITION", out var positionAccessor))
                    throw new InvalidDataException($"{baseName}: POSITION is missing.");

                var positions = ReadVec3Accessor(positionAccessor.GetInt32(), accessors, bufferViews, binary);
                var normals = attributes.TryGetProperty("NORMAL", out var normalAccessor)
                    ? ReadVec3Accessor(normalAccessor.GetInt32(), accessors, bufferViews, binary)
                    : Array.Empty<Vector3D>();
                var indices = primitive.TryGetProperty("indices", out var indexAccessor)
                    ? ReadIndexAccessor(indexAccessor.GetInt32(), accessors, bufferViews, binary)
                    : Enumerable.Range(0, positions.Length).ToArray();

                var geometry = new MeshGeometry3D();
                var bounds = EmptyBounds();
                for (var i = 0; i < positions.Length; i++)
                {
                    var transformed = world.TransformPoint(positions[i]);
                    geometry.Positions.Add(transformed);
                    Include(ref bounds, transformed);
                }

                if (normals.Length == positions.Length)
                {
                    foreach (var normal in normals)
                    {
                        var transformed = world.TransformVector(normal);
                        if (transformed.LengthSquared > 0.0000001)
                            transformed.Normalize();
                        geometry.Normals.Add(transformed);
                    }
                }

                foreach (var index in indices)
                    geometry.TriangleIndices.Add(index);

                geometry.Freeze();
                var partName = primitiveNumber == 0 ? baseName : $"{baseName}#{primitiveNumber}";
                var center = new Point3D(
                    bounds.X + bounds.SizeX / 2d,
                    bounds.Y + bounds.SizeY / 2d,
                    bounds.Z + bounds.SizeZ / 2d);
                parts.Add(new RigidMeshPart(partName, geometry, center, bounds));
                primitiveNumber++;
            }
        }

        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
                VisitNode(child.GetInt32(), world, nodes, meshes, accessors, bufferViews, binary, parts);
        }
    }

    private static Point3D[] ReadVec3Accessor(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var accessor = accessors[accessorIndex];
        if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.GetProperty("type").GetString() != "VEC3")
            throw new InvalidDataException("Expected FLOAT VEC3 accessor.");

        var count = accessor.GetProperty("count").GetInt32();
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        var start = GetOptionalInt(view, "byteOffset") + GetOptionalInt(accessor, "byteOffset");
        var stride = view.TryGetProperty("byteStride", out var strideProperty) ? strideProperty.GetInt32() : 12;
        if (stride < 12)
            throw new InvalidDataException("Invalid VEC3 byteStride.");

        var result = new Point3D[count];
        var span = binary.Span;
        for (var i = 0; i < count; i++)
        {
            var item = start + i * stride;
            EnsureRange(span.Length, item, 12);
            result[i] = new Point3D(
                ReadSingle(span, item),
                ReadSingle(span, item + 4),
                ReadSingle(span, item + 8));
        }
        return result;
    }

    private static Vector3D[] ReadVec3AccessorAsVectors(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var points = ReadVec3Accessor(accessorIndex, accessors, views, binary);
        return points.Select(p => new Vector3D(p.X, p.Y, p.Z)).ToArray();
    }

    private static int[] ReadIndexAccessor(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary)
    {
        var accessor = accessors[accessorIndex];
        if (accessor.GetProperty("type").GetString() != "SCALAR")
            throw new InvalidDataException("Index accessor must be SCALAR.");
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var componentBytes = componentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            _ => throw new InvalidDataException($"Unsupported index component type {componentType}.")
        };
        var count = accessor.GetProperty("count").GetInt32();
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        var start = GetOptionalInt(view, "byteOffset") + GetOptionalInt(accessor, "byteOffset");
        var stride = view.TryGetProperty("byteStride", out var strideProperty) ? strideProperty.GetInt32() : componentBytes;
        var result = new int[count];
        var span = binary.Span;
        for (var i = 0; i < count; i++)
        {
            var item = start + i * stride;
            EnsureRange(span.Length, item, componentBytes);
            result[i] = componentType switch
            {
                5121 => span[item],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(item, 2)),
                5125 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(item, 4))),
                _ => 0
            };
        }
        return result;
    }

    private static Vector3D[] ReadVec3Accessor(int accessorIndex, JsonElement accessors, JsonElement views, ReadOnlyMemory<byte> binary, bool asVector)
        => ReadVec3AccessorAsVectors(accessorIndex, accessors, views, binary);

    private static Matrix4 ReadNodeMatrix(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrix))
        {
            var values = matrix.EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (values.Length != 16)
                throw new InvalidDataException("Node matrix must contain 16 values.");
            return new Matrix4(values);
        }

        var result = Matrix4.Identity;
        if (node.TryGetProperty("scale", out var scale))
        {
            var v = scale.EnumerateArray().Select(value => value.GetDouble()).ToArray();
            result = Matrix4.Multiply(result, Matrix4.Scale(v[0], v[1], v[2]));
        }
        if (node.TryGetProperty("rotation", out var rotation))
        {
            var q = rotation.EnumerateArray().Select(value => value.GetDouble()).ToArray();
            result = Matrix4.Multiply(result, Matrix4.Quaternion(q[0], q[1], q[2], q[3]));
        }
        if (node.TryGetProperty("translation", out var translation))
        {
            var v = translation.EnumerateArray().Select(value => value.GetDouble()).ToArray();
            result = Matrix4.Multiply(Matrix4.Translation(v[0], v[1], v[2]), result);
        }
        return result;
    }

    private static int GetOptionalInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));

    private static void EnsureRange(int length, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > length - count)
            throw new InvalidDataException("Accessor points outside the GLB BIN chunk.");
    }

    private static Rect3D EmptyBounds() => Rect3D.Empty;

    private static void Include(ref Rect3D bounds, Point3D point)
    {
        if (bounds.IsEmpty)
            bounds = new Rect3D(point, new Size3D(0, 0, 0));
        else
            bounds.Union(point);
    }

    private static void Union(ref Rect3D bounds, Rect3D other)
    {
        if (other.IsEmpty) return;
        if (bounds.IsEmpty) bounds = other;
        else bounds.Union(other);
    }

    private readonly struct Matrix4
    {
        private readonly double[] _m;

        public Matrix4(double[] values) => _m = values;

        public static Matrix4 Identity => new(new double[]
        {
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        });

        public static Matrix4 Multiply(Matrix4 a, Matrix4 b)
        {
            var result = new double[16];
            for (var column = 0; column < 4; column++)
            for (var row = 0; row < 4; row++)
            {
                var value = 0d;
                for (var k = 0; k < 4; k++)
                    value += a._m[k * 4 + row] * b._m[column * 4 + k];
                result[column * 4 + row] = value;
            }
            return new Matrix4(result);
        }

        public Point3D TransformPoint(Point3D p)
            => new(
                _m[0] * p.X + _m[4] * p.Y + _m[8] * p.Z + _m[12],
                _m[1] * p.X + _m[5] * p.Y + _m[9] * p.Z + _m[13],
                _m[2] * p.X + _m[6] * p.Y + _m[10] * p.Z + _m[14]);

        public Vector3D TransformVector(Vector3D v)
            => new(
                _m[0] * v.X + _m[4] * v.Y + _m[8] * v.Z,
                _m[1] * v.X + _m[5] * v.Y + _m[9] * v.Z,
                _m[2] * v.X + _m[6] * v.Y + _m[10] * v.Z);

        public static Matrix4 Translation(double x, double y, double z)
        {
            var m = Identity._m.ToArray();
            m[12] = x; m[13] = y; m[14] = z;
            return new Matrix4(m);
        }

        public static Matrix4 Scale(double x, double y, double z)
            => new(new double[]
            {
                x, 0, 0, 0,
                0, y, 0, 0,
                0, 0, z, 0,
                0, 0, 0, 1
            });

        public static Matrix4 Quaternion(double x, double y, double z, double w)
        {
            var length = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (length <= 0.0000001) return Identity;
            x /= length; y /= length; z /= length; w /= length;
            var xx = x * x; var yy = y * y; var zz = z * z;
            var xy = x * y; var xz = x * z; var yz = y * z;
            var wx = w * x; var wy = w * y; var wz = w * z;
            return new Matrix4(new double[]
            {
                1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0,
                2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0,
                2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0,
                0, 0, 0, 1
            });
        }
    }
}
