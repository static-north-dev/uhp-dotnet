#!/bin/sh
set -eu
test "$CLAUDE_CONFIG_DIR" != "$PWD"
if test "$1" = auth; then
  test "$2|$3" = 'status|--json'
  test -z "${ANTHROPIC_API_KEY+x}"
  printf '{"loggedIn":true,"authMethod":"claude.ai","subscriptionType":"team","configDirectory":"%s"}\n' "$CLAUDE_CONFIG_DIR"
  exit
fi

case "$1" in
  --bare)
    test "$ANTHROPIC_API_KEY" = fixture-key
    shift 13
    subscription=false
    ;;
  --safe-mode)
    test "$2" = --disable-slash-commands
    test -z "${ANTHROPIC_API_KEY+x}${ANTHROPIC_AUTH_TOKEN+x}${CLAUDE_CODE_OAUTH_TOKEN+x}"
    shift 14
    subscription=true
    ;;
  *) exit 1 ;;
esac
session=a71cc354-bbe8-4e8e-b3a4-cc994afab3c1
resume=
if test "$1" = --resume; then
  resume=$2
  shift 2
  if "$subscription"; then
    session=$(cat "$PWD/native-id")
  fi
elif "$subscription"; then
  test "$1" = --session-id
  session=$2
  shift 2
fi
test "$1|$2" = '--model|fixture-claude'
shift 2
prompt=$(cat)
printf '%s\n' '{"type":"system","subtype":"init","model":"fixture-claude"}'
case "$prompt" in
  start)
    test -z "$resume"
    test "$#" -eq 2
    test "$1" = --append-system-prompt-file
    test "$(cat "$2")" = 'fixture instructions'
    : > "$CLAUDE_CONFIG_DIR/session"
    : > "$PWD/keep"
    if "$subscription"; then
      printf '%s\n' "$session" > "$PWD/native-id"
      : > "$CLAUDE_CONFIG_DIR/projects/fixture/$session.jsonl"
    fi
    printf '%s\n' '{"type":"stream_event","event":{"delta":{"type":"text_delta","text":"Claude "}}}'
    while ! test -f "$PWD/release"; do /bin/sleep 0.02; done
    printf '%s\n' '{"type":"stream_event","event":{"delta":{"type":"text_delta","text":"response"}}}'
    result='Claude response'
    ;;
  follow|hold)
    test "$resume" = "$session"
    test "$#" -eq 0
    test -f "$CLAUDE_CONFIG_DIR/session"
    test -f "$PWD/keep"
    if test "$prompt" = hold; then
      printf '%s\n' '{"type":"stream_event","event":{"delta":{"type":"text_delta","text":"waiting"}}}'
      exec /bin/sleep 60
    fi
    result='Claude continuation'
    ;;
  *) exit 1 ;;
esac
printf '{"type":"result","subtype":"success","is_error":false,"session_id":"%s","result":"%s"}\n' "$session" "$result"
