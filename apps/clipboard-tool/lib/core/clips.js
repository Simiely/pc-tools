// lib/core/clips.js - 条目域聚合入口（v0.6.14 P2 模块化拆分）
// 实现按子域拆分到同目录单文件，本文件仅聚合 re-export——对外 import 路径与行为不变：
//   clips-store.js    底层存取 + 滚动归档 + 共享内部函数（唯一数据底座）
//   clips-mutate.js   CRUD + 归档/恢复 + 复制计数/置顶 + 过期清扫
//   clips-query.js    列表/搜索/标签统计/标签管理
//   clips-transfer.js 导出/导入
//   tombstones.js     墓碑（WebDAV 同步配套）
// 依赖方向：mutate/query/transfer/tombstones → clips-store → store/config（单向，无循环）
export * from "./clips-store.js";
export * from "./clips-mutate.js";
export * from "./clips-query.js";
export * from "./clips-transfer.js";
export * from "./tombstones.js";
