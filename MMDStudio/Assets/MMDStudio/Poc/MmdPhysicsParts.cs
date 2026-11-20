using System;
using System.Collections.Generic;
using System.Linq;
using UMT;

namespace MmdStudio.Poc
{
    // PH-04: classifies a model's physics bones into user-facing parts from rigid-body and bone
    // names (Japanese, Chinese and English conventions). Bones without a rigid body of their own
    // (after-physics bones) take the part of their nearest classified ancestor.
    internal static class MmdPhysicsParts
    {
        internal static readonly string[] Order = { "hair", "cloth", "chest", "hip", "accessory", "other" };
        internal static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            ["hair"] = "머리카락", ["cloth"] = "옷·치마", ["chest"] = "가슴", ["hip"] = "엉덩이", ["accessory"] = "장신구", ["other"] = "기타",
        };

        private static readonly (string part, string[] keys)[] Keys =
        {
            ("accessory", new[] { "飾", "饰", "アクセ", "イヤリング", "ピアス", "帽", "しっぽ", "尻尾", "尾巴", "tail", "hat" }),
            ("chest", new[] { "胸", "おっぱい", "乳", "bust", "breast" }),
            ("hip", new[] { "尻", "ケツ", "臀", "屁股", "hip", "butt" }),
            ("hair", new[] { "髪", "发", "髮", "もみあげ", "アホ毛", "ツイン", "hair" }),
            ("cloth", new[] { "スカート", "裾", "すそ", "袖", "服", "コート", "マント", "クローク", "ケープ", "マフラー", "リボン", "ネクタイ", "フード",
                              "裙", "衣", "带", "skirt", "cloth", "ribbon", "coat", "cloak", "cape", "hem", "sleeve" }),
        };

        internal static string Classify(string bodyName, string boneName)
        {
            foreach (var (part, keys) in Keys)
                if (keys.Any(key => (bodyName ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (boneName ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
                    return part;
            return "other";
        }

        // Part of every model bone that belongs to a non-kinetic rigid body, propagated to
        // descendants without one. Bones unrelated to physics map to null.
        internal static string[] BoneParts(PMXModel model)
        {
            var parts = new string[model.bones.Length];
            foreach (var body in model.rigidBodies)
            {
                if (body.mode == PMXRigidBody.Mode.Kinetic || body.relatedBoneIndex < 0 || body.relatedBoneIndex >= parts.Length) continue;
                parts[body.relatedBoneIndex] = Classify(body.originalName.ToString(), model.bones[body.relatedBoneIndex].originalName.ToString());
            }
            var result = new string[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                for (int b = i, guard = 0; b >= 0 && b < parts.Length && guard < 512; b = model.bones[b].parentBoneIndex, guard++)
                    if (parts[b] != null) { result[i] = parts[b]; break; }
            }
            return result;
        }
    }
}
