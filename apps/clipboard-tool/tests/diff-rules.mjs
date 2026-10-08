// tests/diff-rules.mjs - 双版本规则一致性差分(试点:去重+排序;扩展:cleanUrl+expiry)
// 目标:把 AGENTS.md 人工铁律"改一端规则必须同步另一端"变成机器门禁。
// 方法:同一份 fixtures(tests/fixtures/rules.json)喂给两端**各自的生产代码**:
//   - Web 侧: 排序/URL清理/过期判定 = lib/core/clips-store.js(sortClips/cleanUrl/isExpired 直接 import 生产模块);
//             去重 = public/app.js findDuplicateClip(vm 从生产源文本提取执行,零复制、无 DOM 依赖);
//   - exe 侧: clipboard-exe/tools/difftest(链接生产源文件编译的 runner)。
// 输出逐 fixture 对拍,不一致 → exit 1。
// 用法: node tests/diff-rules.mjs            (difftest.exe 需已 dotnet build -c Release)
//       EXE=path node tests/diff-rules.mjs   (自定义 difftest 路径)
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";
import { sortClips, cleanUrl, isExpired } from "../lib/core/clips-store.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.dirname(here);
const FIXTURES = path.join(here, "fixtures", "rules.json");
const EXE = process.env.EXE || path.join(root, "clipboard-exe", "tools", "difftest", "bin", "Release", "net9.0", "difftest.exe");

const fixtures = JSON.parse(readFileSync(FIXTURES, "utf8"));

// ---------- Web 侧 ----------
// 去重:从 public/app.js 提取 findDuplicateClip 函数体(括号配对),vm 内执行。
// 若函数被改名/移动/改造导致提取失败 → 抛错(测试红),即"机器检查"能感知漂移。
function extractFunction(src, fnName) {
  const start = src.indexOf("function " + fnName + "(");
  if (start < 0) throw new Error(`[diff-rules] 未在 public/app.js 找到 function ${fnName} —— 规则被改名/移动?`);
  let i = src.indexOf("{", start);
  if (i < 0) throw new Error(`[diff-rules] function ${fnName} 缺少函数体 {`);
  let depth = 0;
  for (; i < src.length; i++) {
    const ch = src[i];
    if (ch === "{") depth++;
    else if (ch === "}") { depth--; if (depth === 0) return src.slice(start, i + 1); }
  }
  throw new Error(`[diff-rules] function ${fnName} 括号未闭合`);
}

const appSrc = readFileSync(path.join(root, "public", "app.js"), "utf8");
const ctx = vm.createContext({});
vm.runInContext(extractFunction(appSrc, "findDuplicateClip") + "\nthis.__find = findDuplicateClip;", ctx);
const findDuplicateClip = ctx.__find;

const web = { d: {}, s: {}, u: {}, e: {} };
for (const fx of fixtures.dedup) {
  const hit = findDuplicateClip(fx.probe, fx.clips);
  web.d[fx.name] = hit ? hit.id : null;
}
for (const fx of fixtures.sort) {
  web.s[fx.name] = sortClips(fx.clips).map(c => c.id);
}
for (const fx of fixtures.cleanUrl || []) {
  web.u[fx.name] = cleanUrl(fx.input);
}
for (const fx of fixtures.expiry || []) {
  web.e[fx.name] = isExpired(fx.clip);
}

// ---------- exe 侧(difftest) ----------
if (!existsSync(EXE)) {
  console.error("[diff-rules] 未找到 " + EXE);
  console.error("  请先构建: cd clipboard-exe/tools/difftest && dotnet build -c Release");
  process.exit(2);
}
const out = execFileSync(EXE, [FIXTURES], { encoding: "utf8" });
const exe = { d: {}, s: {}, u: {}, e: {} };
for (const line of out.split(/\r?\n/)) {
  if (!line.trim()) continue;
  const r = JSON.parse(line);
  if (r.t === "d") exe.d[r.n] = r.id;
  else if (r.t === "s") exe.s[r.n] = r.ids;
  else if (r.t === "u") exe.u[r.n] = r.v;
  else if (r.t === "e") exe.e[r.n] = r.v;
}

// ---------- 对拍 ----------
let failed = 0;
function compare(kind, name, a, b) {
  if (JSON.stringify(a) !== JSON.stringify(b)) {
    failed++;
    console.error(`[FAIL] ${kind}/${name}`);
    console.error(`  web: ${JSON.stringify(a)}`);
    console.error(`  exe: ${JSON.stringify(b)}`);
  }
}
for (const fx of fixtures.dedup) {
  const n = fx.name;
  if (!(n in exe.d)) { failed++; console.error(`[FAIL] exe 缺 dedup fixture: ${n}`); continue; }
  compare("dedup", n, web.d[n], exe.d[n]);
}
for (const fx of fixtures.sort) {
  const n = fx.name;
  if (!(n in exe.s)) { failed++; console.error(`[FAIL] exe 缺 sort fixture: ${n}`); continue; }
  compare("sort", n, web.s[n], exe.s[n]);
}
for (const fx of fixtures.cleanUrl || []) {
  const n = fx.name;
  if (!(n in exe.u)) { failed++; console.error(`[FAIL] exe 缺 cleanUrl fixture: ${n}`); continue; }
  compare("cleanUrl", n, web.u[n], exe.u[n]);
}
for (const fx of fixtures.expiry || []) {
  const n = fx.name;
  if (!(n in exe.e)) { failed++; console.error(`[FAIL] exe 缺 expiry fixture: ${n}`); continue; }
  compare("expiry", n, web.e[n], exe.e[n]);
}

const total = fixtures.dedup.length + fixtures.sort.length
  + (fixtures.cleanUrl || []).length + (fixtures.expiry || []).length;
if (failed === 0) {
  console.log(`[PASS] 双版本规则一致: ${total} fixtures(dedup ${fixtures.dedup.length} + sort ${fixtures.sort.length} + cleanUrl ${(fixtures.cleanUrl || []).length} + expiry ${(fixtures.expiry || []).length}) 全部对拍一致`);
  process.exit(0);
} else {
  console.error(`[FAIL] ${failed}/${total} fixtures 不一致 —— 按 AGENTS 铁律需同步两端实现后再跑`);
  process.exit(1);
}
