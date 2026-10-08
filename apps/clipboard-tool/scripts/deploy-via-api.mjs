// scripts/deploy-via-api.mjs - 通过 GitHub Git Data API 推送本地仓库（绕过被墙的 git 协议）
// 适用：github.com 直连被墙、代理未开时，用 api.github.com（可直连）完成推送
// 用法：cd <repo> && node scripts/deploy-via-api.mjs <repo> <commitMsg>
import fs from "node:fs";
import { execSync } from "node:child_process";

const TOKEN = fs.readFileSync(process.env.GH_TOKEN_FILE || "C:/Temp/gh-token.txt", "utf8").trim();
const REPO = process.argv[2];
const MSG = process.argv[3] || "init";
const BASE = "https://api.github.com";
const H = { Authorization: "token " + TOKEN, "Content-Type": "application/json", "User-Agent": "clipboard-deploy", Accept: "application/vnd.github+json" };

async function api(method, path, body) {
  const r = await fetch(BASE + path, { method, headers: H, body: body ? JSON.stringify(body) : undefined });
  const d = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${JSON.stringify(d).slice(0, 300)}`);
  return d;
}

// 1. 已跟踪文件
const files = execSync("git ls-files", { encoding: "utf8" }).trim().split("\n").filter(Boolean);
console.log("待推送文件:", files.length);

// 2. blobs（base64）
const tree = [];
for (const f of files) {
  const content = fs.readFileSync(f);
  const blob = await api("POST", `/repos/${REPO}/git/blobs`, { content: content.toString("base64"), encoding: "base64" });
  tree.push({ path: f, mode: "100644", type: "blob", sha: blob.sha });
  console.log("  blob:", f);
}

// 3. tree
const t = await api("POST", `/repos/${REPO}/git/trees`, { tree });
console.log("tree:", t.sha);

// 4. commit
const now = new Date().toISOString();
const author = { name: "Simiely", email: "Simiely@users.noreply.github.com", date: now };
const commit = await api("POST", `/repos/${REPO}/git/commits`, { message: MSG, tree: t.sha, author, committer: author });
console.log("commit:", commit.sha);

// 5. ref（main 不存在则创建，已存在则强推更新）
try {
  await api("POST", `/repos/${REPO}/git/refs`, { ref: "refs/heads/main", sha: commit.sha });
  console.log("ref refs/heads/main created");
} catch (e) {
  await api("PATCH", `/repos/${REPO}/git/refs/heads/main`, { sha: commit.sha, force: true });
  console.log("ref refs/heads/main updated");
}

console.log("✅ 推送完成:", files.length, "个文件, commit", commit.sha.slice(0, 7));
