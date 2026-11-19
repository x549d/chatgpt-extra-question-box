# Phase 6B 补充：恢复后回答完成事件被丢弃

2026-11-19。共享存储修正后真实多轮验收发现超时，未把后续个别成功当作全部通过。官方 App Server 的 thread/read 元数据表明对应轮已完成；三条真实并发中最大同时 active=3，服务端均完成，而本地仅一条完成，两条 180 秒超时，Thread/账号和数据库完整。

对合成线程只记录事件名称、时间、轮次后缀和本地绑定后缀：恢复后的 `thread/tokenUsage/updated` 先携带上一轮编号，本地 OnClientNotification 在判断方法前就 MatchesOrBindTurnId，错误占用当前 session；随后的 turn/started、delta 与 turn/completed 编号正确，却因不匹配被忽略。例如旧后缀 5f425d67、实际新轮 f262b9d3。无须读取认证信息或完整对话。

修正是在轮次匹配前仅放行 turn/started、item/agentMessage/delta、item/completed、error 和 turn/completed。用量、Hook 与未知元数据不改变轮次绑定。现有真实早到回答/完成仍可绑定，保留取消轮隔离及账号/Thread 规则，没有重写 AnnotationService。

四项有意义的回归：用量、Hook、未知元数据携带旧编号且位于 turn/start 响应之前，均不能干扰续问；另验证真实 delta/completed 先于 start 响应仍完成。旧代码实际 3 失败/1 通过，修复后全量 **106/106**，0 失败、0 跳过，保留全部 102 既有测试。

13:13:59 的采样轮因该缺陷停止，实际 partial-result.json 为 health_pass=false；它与更早的 21.60 分钟存储故障轮均不计入修复后完整 60 分钟。合成失败消息保留，不把它们改成成功、不删除已有记录。最终载荷、真实修复后并发与完整采样结果将在 [最终报告](PHASE-6-ACCEPTANCE.md) 追加。
