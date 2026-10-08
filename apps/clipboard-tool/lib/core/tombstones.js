// lib/core/tombstones.js - 墓碑子域（WebDAV 同步配套）：单独删除的传播记录
// 语义（与 edge-multi-account-cookie 的 WebDAV 设计一致）：
//  - 单独删除 → 记墓碑 { id, deletedAt } → 同步时传播删除（防旧备份把已删条目复活）
//  - 全部清空 → 不记墓碑（清空即"想从网上同步"，下次同步从远端拉回恢复）
// 墓碑独立文件 <uid>.tombstones.json，不改动既有条目文件格式（零迁移）。
// v0.6.14 从 clips.js 拆出（P2 模块化）：只依赖 store/config，无循环依赖。
import path from "node:path";
import { CONFIG } from "./config.js";
import { readJson, writeJson } from "./store.js";

const TOMB_TTL_MS = 90 * 24 * 3600 * 1000; // 墓碑保留 90 天（远小于"删后改"防复活的合理窗口）

function tombFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".tombstones.json");
}

/** 读墓碑（数组，容错） */
export function loadTombstones(userId) {
  const list = readJson(tombFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 写墓碑数组（同步模块也使用） */
export function saveTombstones(userId, list) {
  writeJson(tombFile(userId), list);
}

/** 记一条墓碑（删除条目时调用）；同 id 墓碑保留最新 deletedAt */
export function recordTombstone(userId, clipId) {
  const list = loadTombstones(userId).filter((t) => t.id !== clipId);
  list.push({ id: clipId, deletedAt: Date.now() });
  saveTombstones(userId, list);
}

/** 清理过期墓碑（>90 天，防无限增长） */
export function pruneTombstones(userId) {
  const now = Date.now();
  const kept = loadTombstones(userId).filter((t) => now - t.deletedAt < TOMB_TTL_MS);
  saveTombstones(userId, kept);
  return kept;
}

/** 清空墓碑（全部清空时调用——清空不传播删除） */
export function clearTombstones(userId) {
  writeJson(tombFile(userId), []);
}
