#!/bin/bash
# 用法: history-counts.sh <镜像仓路径>   —— 输出 §1 范围 1e 三项计数 + 辅助计数；任一为非 0 则退出码 1
cd "$1" || exit 1
c0=$(git rev-list --all | wc -l | tr -d ' ')
c1=$(git log --all -p -- 'samples/AppTemplate/*' | wc -l | tr -d ' ')
c2=$(git log --all -p | grep -ciE '[a-z0-9-]+\.[m]olex\.com')
c3=$(git log --all -p | grep -ci '[m]olex')
c4=$(git log --all -p | grep -ciE '[a-z0-9-]+\.(cdu|cduqa)\.')
c5=$(git log --all --format='%an %ae %cn %ce' | grep -ci '[m]olex')
c6=$(git log --all -p | grep -ciE '莫[仕]|mlxnet[d]ev|MLX-CD[U]|10\.221\.16[5]')
echo "commits(all refs)               : $c0"
echo "1e-1 路径 -p 行数               : $c1"
echo "1e-2 *.<源公司域名> 行数        : $c2"
echo "aux  含源公司名的行数           : $c3"
echo "aux  *.cdu(qa). 主机行数        : $c4"
echo "aux  作者/提交者含源公司名      : $c5"
echo "aux  中文名/内部组/网段残留     : $c6"
[ "$c1$c2$c3$c4$c5$c6" = "000000" ] || { echo "FAIL: 存在非 0 计数"; exit 1; }
