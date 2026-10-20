using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Net;
using HappyWorld.Core.World;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 12 part-time jobs: parsing GET /jobs and a payslip, the board prompt, the answer draft and the lessons.</summary>
    public class JobTests
    {
        // shape copied from the backend's JobsService.overview() / present()
        private const string OverviewJson = @"{
          ""jobs"":[
            {""job"":""CASHIER"",""zone"":""PLAZA"",""x"":44,""y"":20,""nameTh"":""พนักงานแคชเชียร์"",""placeTh"":""มินิมาร์ทหน้าลานเมือง"",""skillTh"":""ทอนเงินให้ถูกและใช้เหรียญน้อยที่สุด"",
             ""workerLevel"":1,""unlocked"":true,""tasksPerShift"":5,""wagePerTask"":8,""perfectBonus"":2,""shiftBonus"":10,""produce"":[]},
            {""job"":""FARMHAND"",""zone"":""COUNTRYSIDE"",""x"":30,""y"":26,""nameTh"":""ผู้ช่วยชาวสวน"",""placeTh"":""สวนลุงมีชนบท"",""skillTh"":""คำนวณลัง"",
             ""workerLevel"":1,""unlocked"":true,""tasksPerShift"":5,""wagePerTask"":7,""perfectBonus"":0,""shiftBonus"":10,""produce"":[""MANGO"",""COCONUT"",""STICKY_RICE""]},
            {""job"":""PACKER"",""zone"":""INDUSTRIAL"",""x"":25,""y"":20,""nameTh"":""พนักงานแพ็กของ"",""placeTh"":""โรงงานแพ็กสินค้า"",""skillTh"":""วางแผนแพ็ก"",
             ""workerLevel"":2,""unlocked"":false,""tasksPerShift"":5,""wagePerTask"":10,""perfectBonus"":4,""shiftBonus"":12,""produce"":[]}],
          ""paidShiftsPerDay"":8,""paidShiftsLeft"":7,""resetsAt"":""2026-10-20T17:00:00.000Z"",
          ""worker"":{""skill"":""WORKER"",""level"":1,""xp"":13,""levelXp"":0,""nextLevelXp"":40},
          ""active"":{""id"":""2b0c7a4e-1111-4c2b-9a6a-000000000001"",""job"":""CASHIER"",""status"":""ACTIVE"",""workerLevel"":1,
            ""startedAt"":""2026-10-20T03:00:00.000Z"",""minSeconds"":10,""expiresAt"":""2026-10-20T03:15:00.000Z"",
            ""tasks"":[
              {""kind"":""CASHIER"",""items"":[{""nameTh"":""น้ำดื่ม"",""price"":7},{""nameTh"":""ขนมปัง"",""price"":15}],""total"":22,""paid"":50,""denominations"":[100,50,20,10,5,2,1]},
              {""kind"":""CASHIER"",""items"":[{""nameTh"":""ข้าวกล่อง"",""price"":39}],""total"":39,""paid"":100,""denominations"":[100,50,20,10,5,2,1]}],
            ""result"":null},
          ""recent"":[],
          ""totals"":{""paidShifts"":1,""goldEarned"":60}}";

        private const string FinishJson = @"{""id"":""2b0c7a4e-1111-4c2b-9a6a-000000000002"",""job"":""PACKER"",""status"":""DONE"",""workerLevel"":2,
          ""startedAt"":""2026-10-20T03:00:00.000Z"",""minSeconds"":10,""expiresAt"":""2026-10-20T03:15:00.000Z"",
          ""tasks"":[{""kind"":""PACKER"",""quantity"":12,""boxSizes"":[9,4,1]},{""kind"":""PACKER"",""quantity"":13,""boxSizes"":[9,4,1]}],
          ""result"":{""checks"":[
              {""correct"":true,""perfect"":false,""expected"":[0,3,0],""answer"":[1,0,3]},
              {""correct"":false,""perfect"":false,""expected"":[1,1,0],""answer"":[1,0,0]}],
            ""pay"":{""base"":10,""perfectBonus"":0,""shiftBonus"":0,""skillBonus"":0,""total"":10},""wageGold"":10,""produce"":{},
            ""workerXp"":2,""playerXp"":1,""paid"":true,""playerLevel"":null,""workerLevelAfter"":2,""workerLeveledUp"":false},
          ""replayed"":false,""paidShiftsLeft"":6,""worker"":{""skill"":""WORKER"",""level"":2,""xp"":54,""levelXp"":40,""nextLevelXp"":160},""gold"":610}";

        private static JobsOverview Overview() => HappyWorldApi.Parse<JobsOverview>(new HttpResult { Status = 200, Body = OverviewJson });
        private static JobFinishResult Finish() => HappyWorldApi.Parse<JobFinishResult>(new HttpResult { Status = 200, Body = FinishJson });

        [Fact]
        public void ParsesTheBoardAndAPayslip()
        {
            var o = Overview();
            Assert.Equal(3, o.jobs.Count);
            Assert.False(o.jobs[2].unlocked);
            Assert.Equal(new List<string> { "MANGO", "COCONUT", "STICKY_RICE" }, o.jobs[1].produce);
            Assert.Equal(40, o.worker.nextLevelXp);
            Assert.Equal(2, o.active.tasks.Count);
            Assert.Equal(22, o.active.tasks[0].total);
            Assert.Equal(7, o.active.tasks[0].denominations.Count);
            Assert.Null(o.active.result);

            var f = Finish();
            Assert.Equal(10, f.result.pay.@base);
            Assert.Equal(new List<int> { 0, 3, 0 }, f.result.checks[0].expected);
            Assert.Equal(2, f.worker.level);
            Assert.Equal(610, f.gold);
            Assert.Empty(f.result.produce);
        }

        [Fact]
        public void PromptAppearsOnlyAtTheBoardOfThisZone()
        {
            var jobs = Overview().jobs;
            Assert.Equal("CASHIER", JobPresenter.BoardNear(jobs, "PLAZA", 45f, 21f).job);
            Assert.Null(JobPresenter.BoardNear(jobs, "PLAZA", 30f, 30f));
            Assert.Null(JobPresenter.BoardNear(jobs, "MARKET", 44f, 20f)); // same spot, other zone
            Assert.Equal("ทำงาน: พนักงานแคชเชียร์", JobPresenter.BoardPrompt(jobs[0]));
            Assert.Contains("Lv 2", JobPresenter.BoardPrompt(jobs[2]));
            Assert.True(JobPresenter.AtExploreGate("COUNTRYSIDE", 50f, 13f));
            Assert.False(JobPresenter.AtExploreGate("PLAZA", 50f, 13f));
        }

        [Fact]
        public void DraftBuildsOneAnswerPerTaskInTheTasksShape()
        {
            var d = new ShiftDraft(Overview().active);
            Assert.Equal(7, d.CurrentCounts.Count);
            // change for 50 - 22 = 28: 20 + 5 + 2 + 1
            d.Add(2);
            d.Add(4);
            d.Add(5);
            d.Add(6);
            Assert.Contains("ทอนแล้ว 28 บาท (4 ชิ้น)", d.RunningLine());
            d.Add(6, -5); // never below zero
            Assert.Equal(0, d.CurrentCounts[6]);
            Assert.True(d.Next());
            Assert.True(d.IsLast);
            Assert.False(d.Next());
            d.Add(1);
            d.Add(5);
            d.Add(6);
            d.Clear();
            Assert.Equal(0, d.CurrentCounts.Sum());
            Assert.True(d.Back());
            var answers = d.Answers;
            Assert.Equal(2, answers.Count);
            Assert.Equal(new List<int> { 0, 0, 1, 0, 1, 1, 0 }, answers[0]);
            Assert.Equal(new List<int> { 0, 0, 0, 0, 0, 0, 0 }, answers[1]);
            // the draft hands out copies: changing them does not change the draft
            answers[0][0] = 9;
            Assert.Equal(0, d.Answers[0][0]);
        }

        [Fact]
        public void PromptsAndLessons()
        {
            var cashier = Overview().active.tasks[0];
            Assert.Contains("น้ำดื่ม 7 + ขนมปัง 15 = 22 บาท", JobPresenter.TaskPrompt(cashier, 0, 5));
            Assert.Contains("แบงก์ 50", JobPresenter.TaskPrompt(cashier, 0, 5));
            Assert.Equal("แบงก์ 20", JobPresenter.SlotLabel(cashier, 20));
            Assert.Equal("เหรียญ 5", JobPresenter.SlotLabel(cashier, 5));

            var wrongChange = new JobTaskCheck { correct = false, expected = new List<int> { 0, 0, 1, 0, 1, 1, 1 }, answer = new List<int> { 0, 0, 1, 0, 1, 0, 0 } };
            var lesson = JobPresenter.Explain(cashier, wrongChange);
            Assert.StartsWith("✗", lesson);
            Assert.Contains("ต้องทอน 28 บาท (50 − 22)", lesson);
            Assert.Contains("25 บาท", lesson);
            Assert.Contains("20 + 5 + 2 + 1", lesson);

            var f = Finish();
            var okNotBest = JobPresenter.Explain(f.tasks[0], f.result.checks[0]);
            Assert.StartsWith("✓", okNotBest);
            Assert.Contains("ใช้ 4 ชิ้น", okNotBest);
            Assert.Contains("ใช้แค่ 3: 4×3", okNotBest);

            var farm = new JobTask { kind = "FARMHAND", rowsKg = new List<int> { 5, 5, 1 }, crateKg = 5 };
            var farmLesson = JobPresenter.Explain(farm, new JobTaskCheck { correct = false, expected = new List<int> { 3 }, answer = new List<int> { 2 } });
            Assert.Contains("รวม 11 กก. ÷ ลังละ 5 = 2.2 → ใช้ 3 ลัง", farmLesson);
            Assert.Contains("ลังที่ไม่เต็มก็ต้องใช้", farmLesson);
        }

        [Fact]
        public void PayslipSaysWhatWasPaidAndPracticeShiftsSaySo()
        {
            var r = Finish().result;
            var lines = JobPresenter.PayslipLines(r);
            Assert.Equal("ทำถูก 1/2 งาน", lines[0]);
            Assert.Contains("<b>รับเงิน 10 ทอง</b>", lines);
            Assert.Equal("ได้ค่าแรง 10 ทอง", JobPresenter.FinishToast(r));

            r.paid = false;
            r.wageGold = 0;
            r.produce = new Dictionary<string, int> { ["MANGO"] = 2 };
            lines = JobPresenter.PayslipLines(r);
            Assert.Contains(lines, l => l.Contains("กะฝึกฝน") && l.Contains("10 ทอง"));
            Assert.Contains(lines, l => l.Contains("มะม่วง ×2"));
            Assert.Equal("จบกะฝึกฝนแล้ว", JobPresenter.FinishToast(r));
        }

        [Fact]
        public void BoardLinesShowLimitAndLock()
        {
            var o = Overview();
            Assert.Equal("วันนี้รับค่าแรงได้อีก 7 กะ (วันละ 8 กะ)", JobPresenter.PaidLeftLine(o));
            o.paidShiftsLeft = 0;
            Assert.Contains("ฝึกฝน", JobPresenter.PaidLeftLine(o));
            Assert.Contains("ต้องมีทักษะพนักงาน Lv 2", JobPresenter.JobLine(o.jobs[2]));
            Assert.Contains("ได้ผลผลิตกลับบ้าน", JobPresenter.JobLine(o.jobs[1]));
            Assert.Equal("ทักษะพนักงาน Lv 1  XP 13/40", JobPresenter.WorkerLine(o.worker));
        }
    }
}
