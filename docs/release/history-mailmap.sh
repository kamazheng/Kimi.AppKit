#!/bin/bash
# 生成 filter-repo --mailmap 文件：把历史中所有含源公司名的作者/提交者邮箱映射为 noreply 身份。
# 用法: history-mailmap.sh <镜像仓路径> > mailmap.txt
# 不把映射表写成静态文件：旧邮箱字面量本身就是要清除的内容，入库会让 `git grep` 自命中。
cd "$1" || exit 1
git log --all --format='%ae%n%ce' | grep -i '[m]olex' | sort -u |
  while read -r old; do echo "kzheng <kamazheng@users.noreply.github.com> <$old>"; done
