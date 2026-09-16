#!/usr/bin/env bash
# Promotes a raw clip to a committed test fixture, so a real recorded stroke becomes
# a regression test that runs in CI with no headset and no Unity.
#
#   make promote CLIP=recordings/raw/stroke-20260916-142211.rowclip [NAME=steady-24spm]
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

clip="${CLIP:-}"
[[ -n "$clip" ]] || die "Set CLIP. e.g. make promote CLIP=recordings/raw/stroke-...rowclip"
[[ -f "$clip" ]] || die "No such clip: $clip"

# Cheap sanity check before it becomes a committed fixture; HandClip.Parse does the real
# validation, but catching a truncated pull here saves a confusing test failure later.
head -1 "$clip" | grep -q '^# rowclip 1$' \
  || die "$clip does not start with '# rowclip 1' - is it a truncated or partial pull?"

fixtures="$REPO_ROOT/tests/RowVR.Stroke.Tests/fixtures"
mkdir -p "$fixtures"

name="${NAME:-$(basename "$clip" .rowclip)}"
dest="$fixtures/$name.rowclip"

if [[ -e "$dest" ]]; then
  die "$dest already exists. Pass a different NAME, or delete it first."
fi

cp "$clip" "$dest"
ok "Promoted to tests/RowVR.Stroke.Tests/fixtures/$name.rowclip"
dim "Now add an assertion against it in tests/RowVR.Stroke.Tests/ and 'make test'."
