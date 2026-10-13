#!/usr/bin/env bash
# sync.sh - gỡ commit của Huy khỏi MỌI nhánh local, giữ nguyên code của bạn.
#
# Cách dùng (trong thư mục repo):   bash sync.sh
#
# - Script KHÔNG push gì cả.
# - Nhánh nào bị sửa đều có bản sao nguyên trạng tên bk-sync-<giờ>-<tên nhánh>.
# - Code chưa commit được tạm commit "wip (sync.sh)" rồi tự trả lại như cũ.

REMOTE="${REMOTE:-origin}"
MAIN="${MAIN:-main}"
BAD_COMMITS="${BAD_COMMITS:-b723b4c 81525e7 16479bd 7edf1c5 1ca8db5 8a5db65}"
MARK="wip (sync.sh)"
TS=$(date +%m%d-%H%M%S)

die() { printf '\nDỪNG: %s\n' "$*" >&2; exit 1; }
say() { printf '%s\n' "$*"; }

# ---------- 0. kiểm tra điều kiện ----------
git rev-parse --git-dir >/dev/null 2>&1 || die "đây không phải thư mục git."
GITDIR=$(git rev-parse --git-dir)
for f in rebase-merge rebase-apply MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD; do
  [ -e "$GITDIR/$f" ] && die "repo đang dở một thao tác git ($f). Hoàn tất hoặc --abort rồi chạy lại."
done

ORIG=$(git branch --show-current)
[ -n "$ORIG" ] || die "đang ở detached HEAD. Gõ: git checkout <tên-nhánh> rồi chạy lại."

# đổi các mã commit ngắn thành mã đầy đủ (bỏ qua cái nào máy này chưa có)
BADS=""
for c in $BAD_COMMITS; do
  full=$(git rev-parse --verify -q "$c^{commit}" 2>/dev/null) && BADS="$BADS $full"
done

# ---------- 1. lấy trạng thái server ----------
say "[1/6] fetch từ $REMOTE..."
git fetch "$REMOTE" || die "fetch lỗi (mạng/quyền?)."
OM=$(git rev-parse --verify -q "$REMOTE/$MAIN") || die "không thấy $REMOTE/$MAIN."

for b in $BADS; do
  git merge-base --is-ancestor "$b" "$OM" 2>/dev/null \
    && die "server ($REMOTE/$MAIN) VẪN còn commit của Huy ($b). Chưa reset xong - đợi Huy báo rồi chạy lại."
done

# hàm: in ra commit MỚI NHẤT của Huy nằm trong ref $1 (rỗng nếu không có)
find_base() {
  local ref="$1" cands="" c d tip
  for c in $BADS; do
    git merge-base --is-ancestor "$c" "$ref" 2>/dev/null && cands="$cands $c"
  done
  for c in $cands; do
    tip=1
    for d in $cands; do
      if [ "$c" != "$d" ] && git merge-base --is-ancestor "$c" "$d" 2>/dev/null; then tip=0; break; fi
    done
    if [ $tip = 1 ]; then echo "$c"; return; fi
  done
}

# ---------- 2. cất code đang dở ----------
WIP=0
if [ -n "$(git status --porcelain)" ]; then
  say "[2/6] có code chưa commit -> tạm commit '$MARK' (sẽ tự gỡ ra sau)"
  git add -A && git commit -q --no-verify -m "$MARK" \
    || die "không commit tạm được (thiếu git config user.name / user.email?). Chưa có gì bị thay đổi."
  WIP=1
else
  say "[2/6] working tree sạch, không cần cất gì."
fi

# ---------- 3+4. duyệt từng nhánh local ----------
say "[3/6] kiểm tra tất cả nhánh local..."
FIXED=""; FAILED=""; CLEAN=""
for br in $(git for-each-ref --format='%(refname:short)' refs/heads); do
  case "$br" in bk-sync-*) continue ;; esac
  base=$(find_base "refs/heads/$br")
  if [ -z "$base" ]; then CLEAN="$CLEAN $br"; continue; fi

  bk="bk-sync-$TS-$(printf '%s' "$br" | tr '/' '-')"
  git branch "$bk" "$br" || die "không tạo được bản sao $bk."
  git checkout -q "$br" || die "không checkout được $br."
  say "      $br: có commit của Huy (gốc ${base:0:7}) -> gỡ ra (bản sao: $bk)"
  if git rebase --onto "$OM" "$base" >/dev/null 2>&1; then
    FIXED="$FIXED $br"
  else
    git rebase --abort >/dev/null 2>&1
    FAILED="$FAILED $br"
    say "      $br: CONFLICT - đã hoàn tác, nhánh giữ nguyên như cũ."
  fi
done

git checkout -q "$ORIG" || die "không quay lại được nhánh $ORIG. Gõ: git checkout $ORIG"

# trả lại code đang dở
if [ $WIP = 1 ]; then
  if [ "$(git log -1 --format=%s)" = "$MARK" ]; then
    git reset -q HEAD~1
  else
    say "CẢNH BÁO: không tự gỡ được commit '$MARK'. Tự gõ: git reset HEAD~1 (nếu commit trên cùng đúng là wip)."
  fi
fi

# ---------- 5. đồng bộ main local ----------
say "[4/6] đồng bộ $MAIN local với server..."
if git show-ref -q --verify "refs/heads/$MAIN"; then
  if git merge-base --is-ancestor "refs/heads/$MAIN" "$OM" 2>/dev/null; then
    if [ "$ORIG" = "$MAIN" ]; then
      git merge -q --ff-only "$OM" 2>/dev/null \
        || say "      không kéo $MAIN lên được vì file đang sửa bị đụng. Commit/stash rồi gõ: git merge --ff-only $REMOTE/$MAIN"
    else
      git branch -f "$MAIN" "$OM"
    fi
  else
    say "      $MAIN local có commit riêng chưa lên server -> không động vào."
  fi
fi

# ---------- 6. kiểm tra + báo cáo ----------
say "[5/6] kiểm tra lại:"
for br in $(git for-each-ref --format='%(refname:short)' refs/heads); do
  case "$br" in bk-sync-*) continue ;; esac
  left=$(find_base "refs/heads/$br")
  if [ -n "$left" ]; then
    say "  ✗ $br: VẪN còn commit của Huy (${left:0:7})  -> làm tay hoặc nhắn Huy"
  else
    n=$(git rev-list --count "$OM..refs/heads/$br")
    say "  ✓ $br: sạch ($n commit riêng so với $REMOTE/$MAIN)"
  fi
done

say ""
say "[6/6] việc còn lại:"
TODO=0
for br in $FIXED; do
  [ "$br" = "$MAIN" ] && continue   # KHÔNG BAO GIỜ force-push main
  if git show-ref -q --verify "refs/remotes/$REMOTE/$br"; then
    say "  - '$br' từng push lên server, lịch sử đã đổi, push lại bằng:"
    say "        git push --force-with-lease $REMOTE $br"
    TODO=1
  fi
done
if [ -n "$FAILED" ]; then
  say "  - nhánh CONFLICT (chưa sửa):$FAILED"
  say "    nhờ người rành git, hoặc gửi output này cho Huy. Code của bạn vẫn nguyên."
  TODO=1
fi
DIRTY_REMOTE=""
for r in $(git for-each-ref --format='%(refname:short)' "refs/remotes/$REMOTE"); do
  case "$r" in "$REMOTE"|"$REMOTE/HEAD") continue ;; esac
  [ -n "$(find_base "refs/remotes/$r")" ] && DIRTY_REMOTE="$DIRTY_REMOTE $r"
done
if [ -n "$DIRTY_REMOTE" ]; then
  say "  - nhánh trên SERVER còn dính commit của Huy:$DIRTY_REMOTE"
  say "    (chủ nhánh phải chạy script này rồi push --force-with-lease)"
  TODO=1
fi
[ $TODO = 0 ] && say "  - không còn gì."
say ""
say "Bản sao an toàn: git branch --list 'bk-sync-*'   (xoá sau khi chắc: git branch -D <tên>)"
[ -z "$FAILED" ]