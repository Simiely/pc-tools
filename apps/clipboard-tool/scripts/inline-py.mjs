// 一次性脚本：把 pypinyin 生成的拼音表内联进 index.html（开发期用，运行时不依赖）
import fs from "node:fs";

const table = JSON.parse(fs.readFileSync("C:/Temp/py-table.json", "utf8"));
const htmlPath = new URL("../public/index.html", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
let html = fs.readFileSync(htmlPath, "utf8");

const start = html.indexOf("/** 拼音首字母缩写搜索");
const end = html.indexOf("/** 前端即时过滤");
if (start < 0 || end < start) { console.error("定位失败", start, end); process.exit(1); }

const tableJson = JSON.stringify(table);
const newBlock = `/** 拼音首字母缩写搜索（零依赖）：GB2312 一级汉字 3755 字 → 首字母映射表（pypinyin 生成）。
 *  例："身份"→"sf"；搜索词 "sf" 可命中标题/标签含"身份"的条目。多音字取常用读音，近似即可。 */
const PY_GROUPS = ${tableJson};
const PY_MAP = new Map();
for (const [letter, chars] of Object.entries(PY_GROUPS)) for (const ch of chars) PY_MAP.set(ch, letter);
function pyInitial(ch) { return PY_MAP.get(String(ch)) || ""; }
function strToPy(s) { let r = ""; for (const ch of String(s || "")) r += pyInitial(ch); return r; }

`;

html = html.slice(0, start) + newBlock + html.slice(end);
fs.writeFileSync(htmlPath, html);
console.log("已内联拼音表，新块大小:", newBlock.length, "B");
