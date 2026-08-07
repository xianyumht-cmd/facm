using System;
using System.Collections.Generic;

namespace FACM.Pets
{
    internal sealed class PetDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OriginalName { get; set; }
        public string Description { get; set; }
        public string ModelUrl { get; set; }
        public string ThumbnailUrl { get; set; }
        public string License { get; set; }
        public string AssetId
        {
            get { return "vrm:facm:" + Id; }
        }
        public string PersonaId
        {
            get { return "facm-" + Id; }
        }
    }

    internal static class PetCatalog
    {
        public const string DefaultPetId = "rabbit";

        private static readonly IReadOnlyList<PetDefinition> Pets = new List<PetDefinition>
        {
            Pet("rabbit", "兔兔", "Rabbit", "带兔耳的完整 VRM 角色", "https://arweave.net/RymRtrmhHx_f9ZDvtvIQb1noTHvILdjoTg5G7L2DR-8", "https://arweave.net/wBqJHzcXuHV0NpFcbtcBB4O2kHqdVN0Zv2QO0jHNkdI"),
            Pet("teddy", "泰迪", "Teddy", "布偶风完整 VRM 角色", "https://arweave.net/KbaYR3YmtjweLgEcJAWekeh3MNAlF9ZWOYJkbNfi8MM", "https://arweave.net/RshgOJaFyAdcmupiH7pUl9-WdC-flIEM0NrPqZEtyfU"),
            Pet("cappy", "蘑菇帽", "Cappy", "轻松可爱的卡通 VRM 角色", "https://arweave.net/nj5MQRsykjZVzRifNkrrbYz5i8rdmYLPDy70NjFuaco", "https://arweave.net/P8A6sgpEOhH8RpjxNYeHzWE9R52x0wVpY9vDT_7y5J8"),
            Pet("dinokid", "恐龙少年", "DinoKid", "恐龙主题完整 VRM 角色", "https://arweave.net/T1gkB95XKXAZl_VmU1ozg5Txm--o9nY0Nge3s8zNoBs", "https://arweave.net/qRNTQjqGS9WiZUr-_dpKOBPyM9a6ucbBpRq_5yiz9lY"),
            Pet("coolalien", "酷外星人", "CoolAlien", "科幻外星人 VRM 角色", "https://arweave.net/FB3g343NrNmQrr4V0191V93pbzOVwTiQWF3PEcL4MNg", "https://arweave.net/wOVOC-UMUDiRkoOI3gPq8CRMIFrC1-cxKRqi5wOEUTE"),
            Pet("witch", "女巫", "Witch", "暗色女巫主题 VRM 角色", "https://arweave.net/0YLwWzDkvVWn9ttv2RAdc8bvWcCBjvYPpu7fpjpHYU0", "https://arweave.net/ZxofC0CRXIB40r26MqyHK7tVAjoYs1a6SYBaxeqruwM"),
            Pet("ghost", "幽灵", "Ghost", "幽灵主题完整 VRM 角色", "https://arweave.net/fSy4hx9L9SqiQIKzjhRLhXzDZpQEJA5izCcDej_WJi8", "https://arweave.net/Q2FpwQkrMJTlpbM8ZoB-vgRni9VIXMW2CZfnSqtdJlw"),
            Pet("polybot", "机甲伙伴", "Polybot", "机器人主题完整 VRM 角色", "https://arweave.net/DUR8v-IugXppdMBxPdE1rDO2dZCJJ7ZgBTXSRgPJFNo", "https://arweave.net/PJ-ovenhR5xdQPv_Z1NkujIpPWjfD_7XENdf7yzHZ_0"),
            Pet("astronaut", "宇航员", "Astronaut", "宇航员主题完整 VRM 角色", "https://arweave.net/T0c0z_XEPQHy3vyXz31XB22s_6JTqHdnau8exq_I8tI", "https://arweave.net/Qo512sj7GqyM2wvlubg4aPyA-Hl_VTxRGYiDZ0A4Wx4"),
            Pet("milk", "牛奶人", "Milk", "白色卡通风完整 VRM 角色", "https://arweave.net/X3NJlq8p9AsiUIqZhsmByDssKQGYeAZxnFNI0fSULMI", "https://arweave.net/C5r_C82cPUwxHxPCL2_ZQC6Gr3owsvbQ2Um3Pkb5_sk")
        };

        public static IReadOnlyList<PetDefinition> All
        {
            get { return Pets; }
        }

        public static PetDefinition Get(string id)
        {
            foreach (var pet in Pets)
            {
                if (string.Equals(pet.Id, id, StringComparison.OrdinalIgnoreCase)) return pet;
            }
            return Pets[0];
        }

        private static PetDefinition Pet(string id, string name, string originalName, string description, string modelUrl, string thumbnailUrl)
        {
            return new PetDefinition
            {
                Id = id,
                Name = name,
                OriginalName = originalName,
                Description = description,
                ModelUrl = modelUrl,
                ThumbnailUrl = thumbnailUrl,
                License = "CC0 1.0（100Avatars R1）"
            };
        }
    }
}
