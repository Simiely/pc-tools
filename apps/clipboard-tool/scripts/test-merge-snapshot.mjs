// scripts/test-merge-snapshot.mjs - mergeSnapshots 纯函数单元测试（无需服务/浏览器，node 直跑）
// 覆盖 WebDAV 同步合并裁决的 4 分支 + 边界：
//   ① 双端同 id 按 updatedAt 取新  ② 墓碑 deletedAt > updatedAt → 删除
//   ③ 删后又编辑（updatedAt > deletedAt）→ 保留（防误删核心分支）
//   ④ 两侧墓碑取最新 deletedAt
// 用法：node scripts/test-merge-snapshot.mjs
import { mergeSnapshots } from "../lib/core/webdav.js";

let pass = 0, fail = 0;
const ok = (n, c) => { c ? pass++ : fail++; console.log((c ? "✅" : "❌") + " " + n); };
const clip = (id, updatedAt, extra = {}) => ({ id, content: "c-" + id, updatedAt, ...extra });

// ---------- ① 双端合并：同 id 按 updatedAt 取新 ----------
{
  const local = [clip("a", 100), clip("b", 200)];
  const remote = { clips: [clip("a", 300), clip("c", 150)], tombstones: [] };
  const r = mergeSnapshots(local, [], remote);
  ok("① 远端新 → 取远端(a updatedAt=300)", r.clips.find(c => c.id === "a")?.updatedAt === 300);
  ok("① 本地新 → 保留本地(b updatedAt=200)", r.clips.find(c => c.id === "b")?.updatedAt === 200);
  ok("① 远端独有 → 并入(c)", !!r.clips.find(c => c.id === "c"));
  ok("① 合并计数 3", r.clips.length === 3);
}

// ---------- ② 墓碑裁决：deletedAt > updatedAt → 删除 ----------
{
  const local = [clip("a", 100)];
  const r = mergeSnapshots(local, [{ id: "a", deletedAt: 500 }], null);
  ok("② 墓碑后删除 → 条目移除", !r.clips.some(c => c.id === "a"));
  ok("② 墓碑保留在输出", r.tombstones.some(t => t.id === "a" && t.deletedAt === 500));
}

// ---------- ③ 删后又编辑：updatedAt > deletedAt → 保留（防误删核心） ----------
{
  const local = [clip("a", 600)]; // 删除后用户又编辑了（updatedAt 刷新 > deletedAt）
  const remote = { clips: [], tombstones: [{ id: "a", deletedAt: 500 }] };
  const r = mergeSnapshots(local, [], remote);
  ok("③ 删后又编辑 → 保留条目", !!r.clips.find(c => c.id === "a"));
  ok("③ 对应墓碑不再生效(仍随输出)", !r.clips.some(c => c.id === "a" && false) && r.tombstones.length === 1);
}

// ---------- ④ 两侧墓碑取最新 deletedAt ----------
{
  const local = [clip("a", 50), clip("b", 50)];
  const remote = {
    clips: [clip("b", 900)], // b 在远端被重新编辑（新于墓碑）
    tombstones: [{ id: "a", deletedAt: 300 }],
  };
  const r = mergeSnapshots(local, [{ id: "a", deletedAt: 700 }, { id: "b", deletedAt: 800 }], remote);
  ok("④ 墓碑取最新(a deletedAt=700)", r.tombstones.find(t => t.id === "a")?.deletedAt === 700);
  ok("④ 远端墓碑并入(a 在远端也有墓碑)", r.tombstones.some(t => t.id === "a"));
  ok("④ b 远端新编辑 → 胜过本地墓碑", !!r.clips.find(c => c.id === "b") && r.clips.find(c => c.id === "b").updatedAt === 900);
  ok("④ a 被墓碑删除", !r.clips.some(c => c.id === "a"));
}

// ---------- 边界：远端 404(null) ----------
{
  const local = [clip("a", 100), clip("b", 200)];
  const r = mergeSnapshots(local, [], null);
  ok("⑤ 远端 null → 本地原样", r.clips.length === 2 && r.tombstones.length === 0);
}

// ---------- 边界：归档条目带 archived 标记不被误伤 ----------
{
  const local = [clip("a", 100, { archived: true }), clip("b", 200)];
  const r = mergeSnapshots(local, [], { clips: [clip("a", 150, { archived: true })], tombstones: [] });
  ok("⑥ 归档标记保留", r.clips.find(c => c.id === "a")?.archived === true);
  ok("⑥ 归档条目参与取新(updatedAt=150)", r.clips.find(c => c.id === "a")?.updatedAt === 150);
}

// ---------- 边界：远端空快照(数组为空)不覆盖本地 ----------
{
  const local = [clip("a", 100)];
  const r = mergeSnapshots(local, [], { clips: [], tombstones: [] });
  ok("⑦ 远端空数组 → 本地保留", r.clips.length === 1);
}

// ---------- 边界：localTomb 为空数组 ----------
{
  const r = mergeSnapshots([clip("a", 100)], [], { clips: [], tombstones: [] });
  ok("⑧ localTomb=[] 容错", r.clips.length === 1 && r.tombstones.length === 0);
}

console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
process.exit(fail ? 1 : 0);
