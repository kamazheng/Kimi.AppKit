#!/bin/bash
# 用法: a4-counts.sh <mirror 仓路径>   —— 输出 §1 范围 1e 三项计数 + 辅助计数
cd "$1" || exit 1
echo "commits(all refs)        : $(git rev-list --all | wc -l | tr -d ' ')"
echo "1e-1 路径 -p 行数        : $(git log --all -p -- 'samples/AppTemplate/*' | wc -l | tr -d ' ')"
echo "1e-2 *.example.com 行数    : $(git log --all -p | grep -ciE '[a-z0-9-]+\.Company\.com')"
echo "aux  含 Company 的行数      : $(git log --all -p | grep -ci 'Company')"
echo "aux  *.cdu(qa). 主机行数  : $(git log --all -p | grep -ciE '[a-z0-9-]+\.(cdu|cduqa)\.')"
