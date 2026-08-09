using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Media3D;

namespace FACM.MachineCat3DPrototype;

internal static class MachineCatModelRequirements
{
    private static readonly string[] RequiredTokens =
    {
        "Head", "body",
        "upper.arm.L", "upper.arm.R", "010.arm.L", "010.arm.R", "hand.L", "hand.R",
        "leg.L", "leg.R", "foot.L", "foot.R",
        "nose", "collar", "bell"
    };

    public static void Validate(RigidModel model)
    {
        if (model.Parts.Count < 20)
            throw new InvalidDataException($"模型只有 {model.Parts.Count} 个 Mesh；当前原型需要拆分好的机器猫零件模型。");

        foreach (var token in RequiredTokens)
        {
            if (!model.Parts.Any(part => part.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"模型缺少必要零件: {token}");
        }
    }
}

internal static class MachineCat3DSelfTest
{
    public static int Run()
    {
        try
        {
            var parsed = GlbRigidModelLoader.Load(CreateTriangleGlb());
            if (parsed.Parts.Count != 1 || parsed.Parts[0].Geometry.TriangleIndices.Count != 3)
                throw new InvalidOperationException("GLB parser fixture failed.");
            if (Math.Abs(parsed.Parts[0].Center.Y - 1d) > 0.51d)
                throw new InvalidOperationException("GLB node matrix was not applied.");

            var fixture = CreateRigFixture();
            MachineCatModelRequirements.Validate(fixture);
            var rig = new RigidPartRig(fixture);
            foreach (var state in Enum.GetValues<MotionState>())
            {
                for (var frame = 0; frame < 360; frame++)
                    rig.Apply(state, frame / 120d);
            }

            ValidateDesktopTravel();
            return 0;
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-cat-3d-self-test-error.txt"), exception.ToString()); }
            catch { }
            return 61;
        }
    }

    private static void ValidateDesktopTravel()
    {
        var controller = new DesktopMotionController(deterministicSeed: 20260810, runChance: 1d);
        var area = new Rect(0d, 0d, 1920d, 1040d);
        const double width = 350d;
        const double height = 380d;
        var left = 1400d;
        controller.Reset(area, width, height, left, 0d);

        var totalTravel = 0d;
        var sawWalk = false;
        var sawRun = false;
        var sawTurn = false;
        for (var frame = 1; frame <= 120 * 45; frame++)
        {
            var now = frame / 120d;
            var next = controller.Step(1d / 120d, now, area, width, height, left);
            totalTravel += Math.Abs(next.Left - left);
            left = next.Left;
            sawWalk |= next.State == MotionState.Walk;
            sawRun |= next.State == MotionState.Run;
            sawTurn |= next.State == MotionState.Turn;

            if (!double.IsFinite(left) || left < 13d || left > area.Right - width - 13d)
                throw new InvalidOperationException($"Desktop motion escaped work area at frame {frame}: {left}");
            if (Math.Abs(next.Top - (area.Bottom - height + 48d)) > 0.001d)
                throw new InvalidOperationException("Desktop motion no longer stays on the ground line.");
        }

        if (totalTravel < 900d)
            throw new InvalidOperationException($"Desktop motion did not really travel: {totalTravel:0.0}px");
        if (!sawWalk || !sawTurn || !sawRun)
            throw new InvalidOperationException("Desktop motion did not exercise walk/turn/run states.");
    }

    public static RigidModel CreateRigFixture()
    {
        var names = new[]
        {
            "000.Head__0", "001.eye.L__0", "001_.eye._R__0", "002.nose__0", "003_.mouth__0",
            "004.moustache.L.1__0", "004.moustache.R.1__0", "005.collar__0", "006.bell.1__0",
            "006.bell.2__0", "007.body__0", "008.bag.1__0", "008.bag.2__0",
            "009.upper.arm.L__0", "009.upper.arm.R__0", "010.arm.L__0", "010.arm.R__0",
            "011.hand.L__0", "011.hand.R__0", "012.leg.L__0", "012.leg.R__0",
            "013.foot.L__0", "013.foot.R__0", "014.tail.1__0", "014.tail.2__0"
        };

        var geometry = new MeshGeometry3D();
        geometry.Positions.Add(new Point3D(-0.08, -0.08, 0));
        geometry.Positions.Add(new Point3D(0.08, -0.08, 0));
        geometry.Positions.Add(new Point3D(0, 0.08, 0));
        geometry.TriangleIndices.Add(0);
        geometry.TriangleIndices.Add(1);
        geometry.TriangleIndices.Add(2);
        geometry.Freeze();
        var bounds = new Rect3D(-0.08, -0.08, 0, 0.16, 0.16, 0.001);
        var parts = names.Select(name => new RigidMeshPart(name, geometry, new Point3D(), bounds)).ToArray();
        return new RigidModel(parts, bounds);
    }

    private static byte[] CreateTriangleGlb()
    {
        var binary = new byte[80];
        var floats = new float[]
        {
            -0.5f, 0f, 0f, 0.5f, 0f, 0f, 0f, 1f, 0f,
            0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f
        };
        for (var i = 0; i < floats.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(binary.AsSpan(i * 4, 4), BitConverter.SingleToInt32Bits(floats[i]));
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(72, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(74, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(76, 2), 2);

        var jsonObject = new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[]
            {
                new
                {
                    name = "test-body",
                    mesh = 0,
                    matrix = new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,1,0,1 }
                }
            },
            meshes = new[]
            {
                new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2 } } }
            },
            buffers = new[] { new { byteLength = 78 } },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = 36 },
                new { buffer = 0, byteOffset = 36, byteLength = 36 },
                new { buffer = 0, byteOffset = 72, byteLength = 6 }
            },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" },
                new { bufferView = 1, componentType = 5126, count = 3, type = "VEC3" },
                new { bufferView = 2, componentType = 5123, count = 3, type = "SCALAR" }
            }
        };

        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jsonObject));
        var jsonPaddedLength = (json.Length + 3) & ~3;
        var total = 12 + 8 + jsonPaddedLength + 8 + binary.Length;
        var glb = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(0, 4), 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8, 4), (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12, 4), (uint)jsonPaddedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16, 4), 0x4E4F534A);
        json.CopyTo(glb.AsSpan(20));
        for (var i = 20 + json.Length; i < 20 + jsonPaddedLength; i++) glb[i] = 0x20;
        var binHeader = 20 + jsonPaddedLength;
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binHeader, 4), (uint)binary.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binHeader + 4, 4), 0x004E4942);
        binary.CopyTo(glb.AsSpan(binHeader + 8));
        return glb;
    }
}
