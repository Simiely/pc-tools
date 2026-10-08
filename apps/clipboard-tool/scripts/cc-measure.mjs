// scripts/cc-measure.mjs - 圈复杂度/认知复杂度量化测量（AST 级，非正则猜测）
// 用法: node scripts/cc-measure.mjs [file...]
// 原理: 逐 token 扫描，按 McCabe 定义统计每个函数的决策点:
//   CC = 1 + if/else if/for/while/do/case/catch/&&/||/? 数量
//   认知复杂度近似 = 嵌套加权（每个决策点按嵌套深度累加）
// 说明: 手写 tokenizer 权衡精度与零依赖；结果用于横向对比与趋势判断，非审计级精确值
import fs from "node:fs";

const files = process.argv.slice(2);
if (!files.length) { console.error("用法: node scripts/cc-measure.mjs <file...>"); process.exit(1); }

// ---------- 极简 tokenizer：剥离字符串/注释/正则字面量，保留结构 token ----------
function tokenize(src) {
  const tokens = [];
  let i = 0, n = src.length, line = 1;
  const push = (t, l) => tokens.push({ t, l });
  while (i < n) {
    const c = src[i];
    if (c === "\n") { line++; i++; continue; }
    // 空白
    if (/\s/.test(c)) { i++; continue; }
    // 行注释
    if (c === "/" && src[i + 1] === "/") { while (i < n && src[i] !== "\n") i++; continue; }
    // 块注释
    if (c === "/" && src[i + 1] === "*") { i += 2; while (i < n && !(src[i] === "*" && src[i + 1] === "/")) { if (src[i] === "\n") line++; i++; } i += 2; continue; }
    // 字符串
    if (c === '"' || c === "'" || c === "`") {
      const q = c; i++;
      while (i < n) {
        if (src[i] === "\\") { i += 2; continue; }
        if (src[i] === "\n" && q !== "`") { line++; i++; break; }
        if (src[i] === q) { i++; break; }
        if (src[i] === "\n") line++;
        i++;
      }
      continue;
    }
    // 正则字面量（粗略：/ 开头且非注释、非除号；用前一个 token 判断）
    if (c === "/") {
      const prev = tokens[tokens.length - 1]?.t;
      const likelyRegex = prev && /[=(,:!&|?{}\[\];]/.test(prev);
      if (likelyRegex) {
        i++;
        let inCls = false;
        while (i < n) {
          if (src[i] === "\\") { i += 2; continue; }
          if (src[i] === "[") inCls = true;
          if (src[i] === "]" ) inCls = false;
          if (src[i] === "/" && !inCls) { i++; break; }
          if (src[i] === "\n") break;
          i++;
        }
        continue;
      }
    }
    // 标识符/数字/多字符操作符
    const m = /^[A-Za-z_$][\w$]*|^\d+(?:\.\d+)?|^=>|^[<>!=]=|^&&|^\|\||^\?\?|^[{}()[\];,.:?+\-*/%<>=!&|]/.exec(src.slice(i));
    if (m) {
      push(m[0], line);
      i += m[0].length;
      continue;
    }
    i++;
  }
  return tokens;
}

// ---------- 函数切分 + CC 计算 ----------
function analyze(src) {
  const tokens = tokenize(src);
  // 找函数边界: function 关键字 / 箭头函数 / 方法简写
  const funcs = [];
  const depthStack = []; // 保存每个 { 的深度
  let depth = 0;
  const braceLines = new Map(); // depth -> line
  for (let idx = 0; idx < tokens.length; idx++) {
    const { t, l } = tokens[idx];
    if (t === "{") {
      depthStack.push({ depth, line: l });
      depth++;
    } else if (t === "}") {
      if (depthStack.length) {
        const start = depthStack.pop();
        // 该块若属于函数体，则由下方函数检测闭合
        depth--;
      }
    }
  }
  // 简化方案：按 function 关键字 + 箭头 + 方法简写收集起点，配对 {} 闭合
  const starts = [];
  for (let idx = 0; idx < tokens.length; idx++) {
    const { t, l } = tokens[idx];
    if (t === "function") {
      // function name( 或 function ( 或 async function
      let name = "";
      let j = idx + 1;
      if (tokens[j]?.t === "*") j++;
      if (tokens[j] && /^[A-Za-z_$][\w$]*$/.test(tokens[j].t)) { name = tokens[j].t; j++; }
      // 找参数列表与函数体起点
      let paren = 0, k = j;
      while (k < tokens.length) {
        if (tokens[k].t === "(") paren++;
        else if (tokens[k].t === ")") { paren--; if (paren === 0) break; }
        k++;
      }
      // 找 { (可能跨默认参数/返回类型)
      let b = k + 1;
      while (b < tokens.length && tokens[b].t !== "{") b++;
      if (b < tokens.length) starts.push({ line: l, name: name || "(anon)", bodyStart: b, end: -1 });
      idx = b; // 跳过函数体
    } else if (t === "=>") {
      // 箭头函数: 从前面最近的标识符/)/ 找起
      let b = idx + 1;
      while (b < tokens.length && tokens[b].t !== "{") { b++; if (tokens[b].t === ";") break; }
      if (b < tokens.length && tokens[b].t === "{") {
        starts.push({ line: l, name: "(arrow@L" + l + ")", bodyStart: b, end: -1 });
        idx = b;
      }
    } else if (t === "async" && tokens[idx + 1]?.t === "(") {
      // async (...) => {...} 箭头函数（对象 handler 写法常见）
      let paren = 0, k = idx + 1;
      while (k < tokens.length) {
        if (tokens[k].t === "(") paren++;
        else if (tokens[k].t === ")") { paren--; if (paren === 0) break; }
        k++;
      }
      if (tokens[k + 1]?.t === "=>") {
        let b = k + 2;
        while (b < tokens.length && tokens[b].t !== "{") { b++; if (tokens[b].t === ";") break; }
        if (b < tokens.length && tokens[b].t === "{") {
          starts.push({ line: l, name: "(asyncArrow@L" + l + ")", bodyStart: b, end: -1 });
          idx = b;
        }
      }
    }
  }
  // 匹配闭合括号
  for (const f of starts) {
    let d = 0;
    for (let i = f.bodyStart; i < tokens.length; i++) {
      if (tokens[i].t === "{") d++;
      else if (tokens[i].t === "}") { d--; if (d === 0) { f.end = i; break; } }
    }
  }
  // 每个函数体计算 CC 与认知复杂度
  const results = [];
  for (const f of starts) {
    const body = tokens.slice(f.bodyStart + 1, f.end);
    let cc = 1;
    let cog = 1;
    let d = 0;
    let lastKw = "";
    for (const { t } of body) {
      if (t === "{") d++;
      else if (t === "}") d = Math.max(0, d - 1);
      else if (t === "if" || t === "else" || t === "for" || t === "while" || t === "do" || t === "catch" || t === "case" || t === "?") {
        // else if 合并
        if (t === "else") { /* else 已计入 if 分支，需单独看 else if */ }
        if (!(lastKw === "else" && t === "if")) { cc++; cog += (d + 1); }
        lastKw = t; continue;
      }
      if (t === "&&" || t === "||") { cc++; cog += (d + 1); }
      lastKw = t;
    }
    results.push({ name: f.name, line: f.line, cc, cog, loc: bodyEndLine(f, tokens) });
  }
  return results;
}

/** 函数体行数（用 token 行号差） */
function bodyEndLine(f, tokens) {
  if (f.end <= f.bodyStart) return 0;
  return Math.max(1, tokens[f.end].l - tokens[f.bodyStart].l + 1);
}

// ---------- 主流程 ----------
let all = [];
for (const file of files) {
  const src = fs.readFileSync(file, "utf8");
  const rs = analyze(src).map((r) => ({ file, ...r }));
  all = all.concat(rs);
  console.log(`\n=== ${file} (${rs.length} 个函数) ===`);
  const sorted = [...rs].sort((a, b) => b.cc - a.cc);
  for (const r of sorted) {
    const flag = r.cc <= 10 ? "低" : r.cc <= 20 ? "中" : "⚠️高危";
    console.log(`  L${String(r.line).padStart(4)} ${r.name.padEnd(28)} CC=${String(r.cc).padStart(3)} 认知=${String(r.cog).padStart(3)} LOC=${String(r.loc).padStart(4)} [${flag}]`);
  }
  const low = rs.filter((r) => r.cc <= 10).length;
  const mid = rs.filter((r) => r.cc > 10 && r.cc <= 20).length;
  const high = rs.filter((r) => r.cc > 20).length;
  const maxCC = Math.max(...rs.map((r) => r.cc));
  const small = rs.filter((r) => r.loc <= 20).length;
  console.log(`  分布: ≤10=${low}(${(low / rs.length * 100).toFixed(0)}%)  11-20=${mid}  >20=${high}  最大CC=${maxCC}  LOC≤20=${small}(${(small / rs.length * 100).toFixed(0)}%)`);
}
