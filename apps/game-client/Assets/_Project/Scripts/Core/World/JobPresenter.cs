using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Engine-free helpers for part-time jobs (Phase 12): which job board the player stands at, the task
    /// prompts, the lesson shown for each task and the payslip. Whether an answer is right and what it pays
    /// is always the server's verdict; nothing here decides money. Unit-tested with .NET.
    /// </summary>
    public static class JobPresenter
    {
        /// <summary>How close to a job board (world units) the "work here" prompt appears.</summary>
        public const float BoardRadius = 3f;

        /// <summary>Countryside: the way out to the expedition sites (Phase 9) is a gate in the fields.</summary>
        public const string ExploreGateZone = "COUNTRYSIDE";
        public const float ExploreGateX = 50f, ExploreGateY = 12f;

        public static JobInfo BoardNear(IEnumerable<JobInfo> jobs, string zone, float x, float y)
        {
            if (jobs == null || zone == null) return null;
            JobInfo best = null;
            var bestD = BoardRadius;
            foreach (var j in jobs)
            {
                if (j.zone != zone) continue;
                var d = (float)Math.Sqrt((j.x - x) * (j.x - x) + (j.y - y) * (j.y - y));
                if (d <= bestD)
                {
                    best = j;
                    bestD = d;
                }
            }
            return best;
        }

        public static bool AtExploreGate(string zone, float x, float y) =>
            zone == ExploreGateZone && Math.Abs(x - ExploreGateX) <= BoardRadius && Math.Abs(y - ExploreGateY) <= BoardRadius;

        public static string BoardPrompt(JobInfo j) =>
            j.unlocked ? "ทำงาน: " + j.nameTh : j.nameTh + " (ทักษะพนักงาน Lv " + j.workerLevel + ")";

        /// <summary>Board sign above the workplace.</summary>
        public static string BoardSign(JobInfo j) => "รับสมัคร " + j.nameTh;

        public static string JobLine(JobInfo j)
        {
            var pay = "ค่าแรง " + j.wagePerTask + " ทอง/งาน";
            if (j.perfectBonus > 0) pay += " · ทิป " + j.perfectBonus + " ถ้าทำได้ดีที่สุด";
            if (j.shiftBonus > 0) pay += " · ถูกครบทั้งกะ +" + j.shiftBonus;
            if (j.produce != null && j.produce.Count > 0) pay += " · ได้ผลผลิตกลับบ้าน";
            var lockLine = j.unlocked ? "" : "\n<color=#C0392B>ต้องมีทักษะพนักงาน Lv " + j.workerLevel + "</color>";
            return "<b>" + j.nameTh + "</b>  <size=24>" + j.placeTh + "</size>\n<size=24>ฝึก: " + j.skillTh + "\n" + pay + "</size>" + lockLine;
        }

        public static string PaidLeftLine(JobsOverview o)
        {
            if (o.paidShiftsLeft > 0) return "วันนี้รับค่าแรงได้อีก " + o.paidShiftsLeft + " กะ (วันละ " + o.paidShiftsPerDay + " กะ)";
            return "วันนี้ครบ " + o.paidShiftsPerDay + " กะแล้ว ทำต่อได้แบบฝึกฝน (ไม่มีค่าแรง) พรุ่งนี้รับค่าแรงใหม่";
        }

        public static string WorkerLine(SkillView w)
        {
            var bonus = (w.level - 1) * 5;
            var next = w.nextLevelXp.HasValue ? "  XP " + w.xp + "/" + w.nextLevelXp.Value : "  (สูงสุดแล้ว)";
            return "ทักษะพนักงาน Lv " + w.level + next + (bonus > 0 ? "  ค่าแรง +" + bonus + "%" : "");
        }

        // ---------- tasks ----------

        /// <summary>"น้ำดื่ม 7 + ขนมปัง 15 = 22 บาท"</summary>
        public static string Bill(JobTask t) =>
            string.Join(" + ", t.items.Select(i => i.nameTh + " " + i.price)) + (t.items.Count > 1 ? " = " + t.total : "") + " บาท";

        public static string TaskPrompt(JobTask t, int index, int count)
        {
            var head = "<b>งานที่ " + (index + 1) + "/" + count + "</b>\n";
            switch (t.kind)
            {
                case "CASHIER":
                    return head + "ลูกค้าซื้อ " + Bill(t) + "\nจ่ายมาเป็นแบงก์ " + t.paid + " บาท ทอนเงินให้ถูก (ใช้เหรียญ/แบงก์น้อยที่สุดได้ทิป)";
                case "FARMHAND":
                    return head + "เก็บผลผลิตได้แถวละ " + string.Join(", ", t.rowsKg) + " กก.\nลังหนึ่งใส่ได้ " + t.crateKg + " กก. ต้องใช้กี่ลัง?";
                case "PACKER":
                    return head + "ออร์เดอร์ " + t.quantity + " ชิ้น มีกล่องขนาด " + string.Join(", ", t.boxSizes) + " ชิ้น\nแพ็กให้พอดี (ใช้กล่องน้อยที่สุดได้โบนัส)";
                default:
                    return head + t.kind;
            }
        }

        /// <summary>The sizes the answer counts (coin/note values, box sizes); one slot (crates) for the farm.</summary>
        public static List<int> Slots(JobTask t) =>
            t.kind == "CASHIER" ? t.denominations : t.kind == "PACKER" ? t.boxSizes : new List<int> { 1 };

        public static string SlotLabel(JobTask t, int size) =>
            t.kind == "CASHIER" ? (size >= 20 ? "แบงก์ " : "เหรียญ ") + size : t.kind == "PACKER" ? "กล่อง " + size : "ลัง";

        /// <summary>"20 + 10×2 + 5" (only sizes used).</summary>
        public static string Pieces(IList<int> counts, IList<int> sizes)
        {
            var parts = new List<string>();
            for (int i = 0; i < sizes.Count && i < counts.Count; i++)
                if (counts[i] > 0) parts.Add(sizes[i] + (counts[i] > 1 ? "×" + counts[i] : ""));
            return parts.Count == 0 ? "ไม่มี" : string.Join(" + ", parts);
        }

        public static int Value(IList<int> counts, IList<int> sizes)
        {
            int v = 0;
            for (int i = 0; i < sizes.Count && i < counts.Count; i++) v += counts[i] * sizes[i];
            return v;
        }

        /// <summary>The lesson for one task after the server checked it.</summary>
        public static string Explain(JobTask t, JobTaskCheck c)
        {
            var mark = c.correct ? "✓ " : "✗ ";
            if (t.kind == "FARMHAND")
            {
                var kg = t.rowsKg.Sum();
                var crates = c.expected.Count > 0 ? c.expected[0] : 0;
                var why = "รวม " + kg + " กก. ÷ ลังละ " + t.crateKg + " = " + (kg / (double)t.crateKg).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " → ใช้ " + crates + " ลัง";
                if (c.correct) return mark + why;
                var said = c.answer.Count > 0 ? c.answer[0] : 0;
                return mark + "ตอบ " + said + " ลัง · " + why + (kg % t.crateKg != 0 ? " (ลังที่ไม่เต็มก็ต้องใช้)" : "");
            }
            var sizes = Slots(t);
            var target = t.kind == "CASHIER" ? t.paid - t.total : t.quantity;
            var unit = t.kind == "CASHIER" ? " บาท" : " ชิ้น";
            var bestWay = Pieces(c.expected, sizes);
            var best = c.expected.Sum();
            var used = c.answer.Sum();
            var what = t.kind == "CASHIER" ? "ทอน " : "แพ็ก ";
            if (c.perfect) return mark + what + target + unit + " = " + bestWay + " ดีที่สุดแล้ว";
            if (c.correct)
                return mark + what + "ถูก " + target + unit + " แต่ใช้ " + used + " ชิ้น · แบบนี้ใช้แค่ " + best + ": " + bestWay;
            var given = Value(c.answer, sizes);
            var need = t.kind == "CASHIER" ? "ต้องทอน " + target + unit + " (" + t.paid + " − " + t.total + ")" : "ต้องได้ " + target + unit;
            return mark + need + " คุณ" + what.Trim() + " " + given + unit + " · ทำแบบนี้: " + bestWay;
        }

        // ---------- payslip ----------

        public static string ProduceText(Dictionary<string, int> produce)
        {
            if (produce == null || produce.Count == 0) return null;
            return string.Join(", ", produce.Select(p => Texts.T("mat_" + p.Key.ToLowerInvariant()) + " ×" + p.Value));
        }

        public static List<string> PayslipLines(JobShiftResult r)
        {
            var lines = new List<string>();
            var right = r.checks.Count(c => c.correct);
            lines.Add("ทำถูก " + right + "/" + r.checks.Count + " งาน");
            var p = r.pay;
            lines.Add("ค่าแรง " + p.@base);
            if (p.perfectBonus > 0) lines.Add("ทิปทำได้ดีที่สุด +" + p.perfectBonus);
            if (p.shiftBonus > 0) lines.Add("ถูกครบทั้งกะ +" + p.shiftBonus);
            if (p.skillBonus > 0) lines.Add("โบนัสทักษะพนักงาน +" + p.skillBonus);
            if (r.paid) lines.Add("<b>รับเงิน " + r.wageGold + " ทอง</b>");
            else lines.Add("<b>กะฝึกฝน</b> วันนี้ครบกะที่มีค่าแรงแล้ว (ถ้าเป็นวันปกติจะได้ " + p.total + " ทอง)");
            var produce = ProduceText(r.produce);
            if (produce != null) lines.Add("ผลผลิตกลับบ้าน: " + produce);
            if (r.workerXp > 0) lines.Add("ทักษะพนักงาน +" + r.workerXp + " XP" + (r.workerLeveledUp ? "  เลเวลอัป! Lv " + r.workerLevelAfter : ""));
            return lines;
        }

        public static string FinishToast(JobShiftResult r) =>
            r.paid ? "ได้ค่าแรง " + r.wageGold + " ทอง" + (r.workerLeveledUp ? " · ทักษะพนักงาน Lv " + r.workerLevelAfter : "") : "จบกะฝึกฝนแล้ว";
    }

    /// <summary>
    /// The answers being built during a shift: one count per slot (coin/note, box size, or crates) for every task.
    /// The running line ("ทอนแล้ว 30 บาท") helps the player count; it never says whether the answer is right.
    /// </summary>
    public sealed class ShiftDraft
    {
        public const int MaxPerSlot = 99;
        private readonly JobShift _shift;
        private readonly List<List<int>> _answers;

        public ShiftDraft(JobShift shift)
        {
            _shift = shift;
            _answers = shift.tasks.Select(t => JobPresenter.Slots(t).Select(_ => 0).ToList()).ToList();
        }

        public JobShift Shift => _shift;
        public int Index { get; private set; }
        public int Count => _shift.tasks.Count;
        public JobTask Current => _shift.tasks[Index];
        public List<int> CurrentCounts => _answers[Index];
        public bool IsLast => Index == Count - 1;
        public List<List<int>> Answers => _answers.Select(a => a.ToList()).ToList();

        public void Add(int slot, int n = 1)
        {
            var c = _answers[Index];
            if (slot < 0 || slot >= c.Count) return;
            c[slot] = Math.Max(0, Math.Min(MaxPerSlot, c[slot] + n));
        }

        public void Clear()
        {
            var c = _answers[Index];
            for (int i = 0; i < c.Count; i++) c[i] = 0;
        }

        public bool Next()
        {
            if (Index >= Count - 1) return false;
            Index++;
            return true;
        }

        public bool Back()
        {
            if (Index == 0) return false;
            Index--;
            return true;
        }

        public string RunningLine()
        {
            var t = Current;
            var c = CurrentCounts;
            var sizes = JobPresenter.Slots(t);
            switch (t.kind)
            {
                case "CASHIER":
                    return "ทอนแล้ว " + JobPresenter.Value(c, sizes) + " บาท (" + c.Sum() + " ชิ้น): " + JobPresenter.Pieces(c, sizes);
                case "PACKER":
                    return "แพ็กแล้ว " + JobPresenter.Value(c, sizes) + "/" + t.quantity + " ชิ้น ใช้ " + c.Sum() + " กล่อง";
                default:
                    return "ตอบ " + c[0] + " ลัง";
            }
        }
    }
}
