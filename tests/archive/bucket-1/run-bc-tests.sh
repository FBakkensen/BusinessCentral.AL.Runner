#!/usr/bin/env bash
# Run bc-test against all bucket-1 test codeunits, one at a time (API only supports single CodeunitId).
# Credentials injected via: op run --env-file=.op.env -- bash run-bc-tests.sh
#
# Options forwarded to bc-test: --failures-only, --verbose, --format, --output

: "${BC_USERNAME:?not set — run via: op run --env-file=.op.env -- bash run-bc-tests.sh}"
: "${BC_PASSWORD:?not set — run via: op run --env-file=.op.env -- bash run-bc-tests.sh}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT="${SCRIPT_DIR}/.dev/bc-test-results.json"
mkdir -p "${SCRIPT_DIR}/.dev"

# Write ephemeral .bcconfig.json (deleted on exit)
python3 -c "
import json, os
print(json.dumps({
  'server': 'http://' + os.environ.get('BC_SERVER', 'localhost'),
  'port': 7052,
  'instance': 'BC',
  'tenant': 'default',
  'username': os.environ['BC_USERNAME'],
  'password': os.environ['BC_PASSWORD']
}))
" > "${SCRIPT_DIR}/.bcconfig.json"
trap 'rm -f "${SCRIPT_DIR}/.bcconfig.json"' EXIT

# Discover all test codeunit IDs from source
CODEUNIT_IDS=$(grep -rh "^codeunit " "${SCRIPT_DIR}"/*/*/test/*.al 2>/dev/null \
  | awk '{print $2}' | sort -n | tr '\n' ' ')

if [ -z "$CODEUNIT_IDS" ]; then
  echo "ERROR: No test codeunits found" >&2
  exit 1
fi

TOTAL=$(echo $CODEUNIT_IDS | wc -w)
echo "Found $TOTAL test codeunits. Running each individually..."

PASS=0
FAIL=0
ERRORS=()

for CU_ID in $CODEUNIT_IDS; do
  RESULT=$(cd "${SCRIPT_DIR}" && bc-test "$CU_ID" --format json 2>&1)
  EXIT_CODE=$?
  if [ $EXIT_CODE -eq 0 ]; then
    PASS=$((PASS + 1))
  else
    FAIL=$((FAIL + 1))
    ERRORS+=("$CU_ID")
    echo "FAIL: codeunit $CU_ID"
    echo "$RESULT" | grep -E "FAIL|Error|error" | head -5
  fi
done

echo ""
echo "Results: $PASS passed, $FAIL failed (of $TOTAL codeunits)"
if [ ${#ERRORS[@]} -gt 0 ]; then
  echo "Failed codeunits: ${ERRORS[*]}"
  exit 1
fi
