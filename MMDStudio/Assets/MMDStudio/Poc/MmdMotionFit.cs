using System;
using System.Collections.Generic;
using System.Linq;

namespace MmdStudio.Poc
{
    // D-03 (1): motion/model fit diagnosis in the user's words. A VMD addresses bones and morphs by
    // their Japanese names; whatever the model lacks is not applied. Semi-standard bones (準標準ボーン)
    // are the usual cause: many motions key them and many models do not have them. Each missing name
    // is explained by what the viewer will see. (Runtime completion and foot grounding are
    // investigated in docs/D03_MOTION_FIT.md.)
    public static class MmdMotionFit
    {
        public sealed class Issue
        {
            public string Name, Symptom;
            public bool SemiStandard;
        }
        public sealed class Report
        {
            public List<Issue> Bones = new List<Issue>();
            public int MissingMorphs, MouthMorphs, EyeMorphs;
            public string[] ModelLacksSemiStandard = new string[0];
            public bool Clean => Bones.Count == 0 && MissingMorphs == 0;
        }

        // Semi-standard bones and what is lost without them (left/right share one entry).
        private static readonly (string[] names, string symptom)[] SemiStandard =
        {
            (new[] { "全ての親" }, "전체 이동이 빠져 캐릭터가 제자리에 머뭅니다"),
            (new[] { "グルーブ" }, "몸 전체의 상하 움직임(점프·무릎 굽힘)이 빠집니다"),
            (new[] { "腰" }, "골반 회전이 빠져 허리 움직임이 줄어듭니다"),
            (new[] { "上半身2" }, "가슴 위쪽 비틀림이 빠져 상체가 뻣뻣해 보일 수 있습니다"),
            (new[] { "左足IK親", "右足IK親" }, "발 위치 이동이 빠져 발이 미끄러지거나 다른 곳을 디딜 수 있습니다"),
            (new[] { "左腕捩", "右腕捩" }, "팔 비틀림이 빠져 팔꿈치 쪽이 꼬여 보일 수 있습니다"),
            (new[] { "左手捩", "右手捩" }, "손목 비틀림이 빠져 손목이 꺾여 보일 수 있습니다"),
            (new[] { "左足D", "右足D", "左ひざD", "右ひざD", "左足首D", "右足首D" }, "다리 D본 움직임이 빠집니다(다리 모양이 달라질 수 있음)"),
            (new[] { "両目" }, "두 눈을 함께 돌리는 시선 움직임이 빠집니다"),
        };
        private static readonly string[] MouthMorphs = { "あ", "い", "う", "え", "お", "ん", "ワ", "ω", "▲", "∧", "□", "口角上げ", "口角下げ" };
        private static readonly string[] EyeMorphs = { "まばたき", "笑い", "ウィンク", "ウィンク右", "ウィンク２", "ｳｨﾝｸ２右", "はぅ", "なごみ", "じと目", "びっくり", "瞳小" };
        public static IEnumerable<string> SemiStandardNames => SemiStandard.SelectMany(s => s.names);

        public static Report Diagnose(string[] missingBones, string[] missingMorphs, IEnumerable<string> modelBones)
        {
            var report = new Report();
            foreach (var name in missingBones ?? new string[0])
            {
                var known = SemiStandard.FirstOrDefault(s => s.names.Contains(name));
                report.Bones.Add(new Issue
                {
                    Name = name, SemiStandard = known.names != null,
                    Symptom = known.names != null ? known.symptom : "이 본의 움직임은 적용되지 않습니다",
                });
            }
            var morphs = missingMorphs ?? new string[0];
            report.MissingMorphs = morphs.Length;
            report.MouthMorphs = morphs.Count(m => MouthMorphs.Contains(m));
            report.EyeMorphs = morphs.Count(m => EyeMorphs.Contains(m));
            var bones = new HashSet<string>(modelBones ?? new string[0]);
            report.ModelLacksSemiStandard = SemiStandard.Where(s => !s.names.Any(bones.Contains)).Select(s => s.names[0].Replace("左", "")).ToArray();
            return report;
        }

        // Inspector text: the semi-standard problems first (they explain the visible symptoms).
        public static string Describe(Report report)
        {
            if (report.Clean) return "모션과 모델이 맞습니다: 모션의 모든 본·모프가 모델에 있습니다.";
            var lines = new List<string>();
            var semi = report.Bones.Where(b => b.SemiStandard).GroupBy(b => b.Symptom).ToList();
            foreach (var group in semi) lines.Add("· " + string.Join("·", group.Select(b => b.Name)) + " 없음 → " + group.Key);
            var other = report.Bones.Where(b => !b.SemiStandard).Select(b => b.Name).ToList();
            if (other.Count > 0) lines.Add("· 그 밖의 본 " + other.Count + "개 없음(" + string.Join(", ", other.Take(6)) + (other.Count > 6 ? " 외" : "") + ") → 그 본의 움직임은 적용되지 않습니다");
            if (report.MissingMorphs > 0)
                lines.Add("· 모프 " + report.MissingMorphs + "개 없음" + (report.MouthMorphs > 0 ? " (입 모양 " + report.MouthMorphs + "개 → 립싱크·말하는 입이 빠짐)" : "") +
                    (report.EyeMorphs > 0 ? " (눈 표정 " + report.EyeMorphs + "개 → 깜빡임·표정이 빠짐)" : ""));
            lines.Add("원본 PMX는 바꾸지 않습니다. 준표준 본을 추가한 모델을 쓰면 해결됩니다.");
            return "모션 맞춤 진단:\n" + string.Join("\n", lines);
        }
    }
}
