#!/usr/bin/env bash
# Force-pushes the contents of ./ci (logs, probe output, test results) to the
# orphan branch "ci-results" so they can be inspected without the GitHub API.
set -uo pipefail
mkdir -p ci
{
  echo "commit: ${GITHUB_SHA:-unknown}"
  echo "run: ${GITHUB_RUN_NUMBER:-?} (${GITHUB_SERVER_URL:-}/${GITHUB_REPOSITORY:-}/actions/runs/${GITHUB_RUN_ID:-})"
  echo "build: ${1:-unknown}"
  echo "smoke: ${2:-unknown}"
  date -u
} > ci/STATUS.txt
cp -r out ci/out 2>/dev/null || true

tmp=$(mktemp -d)
cp -r ci "$tmp/"
cd "$tmp"
git init -q
git checkout -q -b ci-results
git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add -A
git commit -qm "CI results for ${GITHUB_SHA:-unknown} (run ${GITHUB_RUN_NUMBER:-?})"
git push -qf "https://x-access-token:${GITHUB_TOKEN}@github.com/${GITHUB_REPOSITORY}.git" ci-results
echo "Published results to ci-results"
