#!/bin/sh
set -eu
test "$1" = app-server
IFS= read -r initialize
case "$initialize" in
  '{"id":1,"method":"initialize","params":'*) ;;
  *) exit 1 ;;
esac
printf '%s\n' '{"id":1,"result":{}}'
IFS= read -r initialized
test "$initialized" = '{"method":"initialized","params":{}}'
IFS= read -r request
case "$request" in
  '{"id":2,"method":"model/list",'*)
    printf '%s\n' '{"id":2,"result":{"data":[{"model":"fixture-codex","isDefault":true}]}}'
    ;;
  '{"id":2,"method":"thread/start",'*)
    printf '%s\n' '{"id":2,"result":{"thread":{"id":"thread"},"model":"fixture-codex"}}'
    IFS= read -r request
    case "$request" in
      '{"id":3,"method":"turn/start",'*'"text":"codex task"'*) ;;
      *) exit 1 ;;
    esac
    printf '%s\n' \
      '{"id":3,"result":{"turn":{"id":"turn"}}}' \
      '{"method":"item/agentMessage/delta","params":{"threadId":"thread","turnId":"turn","delta":"Codex response"}}' \
      '{"method":"turn/completed","params":{"threadId":"thread","turn":{"id":"turn","status":"completed"}}}'
    IFS= read -r request
    test "$request" = '{"id":4,"method":"thread/backgroundTerminals/clean","params":{"threadId":"thread"}}'
    printf '%s\n' '{"id":4,"result":{}}'
    ;;
  *) exit 1 ;;
esac
