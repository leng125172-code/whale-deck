# 回收与恢复任务页提示修复：预构建检查点

2026-11-06。本批验证发布准备，尚未覆盖生产或执行真实任务页复验。活动范围仍为59项，17项移出不算完成。

修复 `f51f4735d5be82b2bc11c9c56fa2bcb85106a662` 将回收/恢复任务的页尾提示与递归ACL提示分开，保留原取消、通用重试的安全判定。冻结发布目标为 `150adfd2681f7b0a53b07c1d2f27bcb1f8721bbe`，运行目录已由仓库用户 fast-forward 到该提交，私有配置保留。运行目录版本不代表实际部署版本。

## 已验证结果

- 四镜像预构建 unit `whaledeck-qa-footer-images-f491976c`、Invocation `2430a0d7eac64a9eb298b39ecaa2b3e9`，终态 exited、Result success、ExecMainCode 1、ExecMainStatus 0。
- 两宿主预构建 unit `whaledeck-qa-footer-hosts-f491976c`、Invocation `3d5327c6e1fe483f87a9f4f17785fc9f`，相同成功终态。没有安装宿主。
- 四镜像 revision label 与隔离只读、无网络 `--build-info`，以及两个宿主实际 apphost 的 `--build-info` 均匹配目标提交。707个宿主文件 SHA256 校验通过，自包含 .NETCore/AspNetCore/Pkcs 为10.0.13。
- 构建前后2346个保护文件、11个核心容器身份、既有业务Root/HDD完整快照保持。29条回收、15条恢复以 PostgreSQL 完整 `to_jsonb` 行文本 SHA256 逐行核对保持，活动操作0。
- 本轮实际重跑任务页定向测试：1文件、25项全部通过，耗时4.42秒。没有新业务写入、信号故障或身份变更。

准确 CI 为 `42131426329`，headSha 与发布目标一致。检查点时 installer/backend/native-valkey/native-postgres/frontend_checks 5项成功，6个浏览器分片仍运行，frontend 汇总尚未产生成功终态。发布门禁要求12个固定任务全部 completed/success；此前版本成功与预构建成功均不替代该门禁。

## 已准备但未执行

私有 `start-release.py` 要求准确12项CI终态、运行目录准确且干净、旧完整业务行/Root-HDD/保护基线保持及活动0，才可启动固定标准维护更新和维护入口采样。`postflight.py` 核对六组件/HTTP、生产迁移/CHECK/围栏、真实宿主exe与DLL映射、旧业务行与Root/HDD，以及受限安装元数据差异。

私有只读页面门禁覆盖现有四类回收/恢复Job，各在桌面明色与320宽暗色打开实际详情：新提示与文件页链接存在，递归ACL旧提示和通用重试按钮缺席，管理写入与retry请求0。计划使用临时真实Authentik管理员、可见登录、正常BFF/OIDC双端退出，前后再核对完整基线。这里只完成脚本语法检查，未创建账号、未打开真实任务页，不能记8场景实机通过。旧递归ACL任务不在本次真实页面签收范围，原定向测试覆盖其提示分流。

准备过程中曾把仓库相对路径用于web工作目录，导致包装脚本写入和测试启动失败；未产生该脚本或执行测试，无生产影响。已纠正路径、保留失败记录，随后脚本准备及25项测试成功。

## 证据和恢复入口

私有证据 `deploy/artifacts/container-recycle-footer-release-f491976c66ad4576bc6a1816fc5ffac6`，父目录0700、文件root:root/0600。`prebuild-checkpoint/` 冻结17份文件，`PREBUILD_CHECKPOINT_SHA256.json` 摘要 `21a0295eef99b2d1a8f1a81c17934d8fa6f31c4ee2f7bade08ce83f7e235aa59`，实际逐文件复核通过。检查点内的request和CI只表示封存时刻；后续进度读取外层request，不重启已完成构建。

当前request阶段 `prebuild-verified-awaiting-ci`。生产两宿主及应用仍为 `3d80d17b7b68c1fd6909a3ff6220b5381217eac7`，没有启动标准覆盖或维护观测。下一步复查准确CI，满足门禁后标准部署、生产核验与真实页尾复验，再推进写后撤权、Unknown等故障矩阵、长期清理及其余目标。

本批不提高整项完成比例：整项0/59、阶段0/6，酒馆/供应商P4-08与Live2D看板娘P5-05功能0%保持，整体25%–35%仍为粗估；可部署master尚未交付。源与HDD设备号均为2049，不称为独立物理盘灾备验收。
