# agterm-win shell integration for bash (Git Bash / MSYS2 / Cygwin).
#
# Reports the working directory to the terminal as OSC 7 on every prompt, which is what keeps
# Session.CurrentCwd live and lets a restored session reopen where it left off.
#
# Attached with `bash --rcfile <this file> -i`. --rcfile REPLACES ~/.bashrc rather than adding to
# it, so sourcing the user's own rc below is not a courtesy — without it a login would silently
# lose everything their rc sets up (PATH entries, aliases, completion).
#
# Deliberately does NOT emit OSC 0/1/2: Session.DisplayName prefers oscTitle over the cwd
# basename, so auto-titling would overwrite every sidebar row with a noisy path (upstream
# disables it for the same reason, `shell-integration-features = no-title`).

# Restore what --rcfile displaced, in bash's own order, before anything else. Failures here are
# the user's own rc misbehaving, so they surface rather than being swallowed.
if [ -f ~/.bashrc ]; then
  . ~/.bashrc
fi

# Nothing to report to: sourcing this outside agterm leaves a plain, fully working shell.
if [ -z "${AGTERM_ENABLED:-}" ]; then
  return 0 2>/dev/null || true
fi

# OSC 7 carries a file:// URI, so the path needs percent-encoding: a space or a '#' would
# otherwise produce a URI the parser rejects and the cwd would silently stop updating.
#
# ':' and '/' are left AS-IS on purpose. The URI must read file:///C:/git/x — the authority is
# empty (three slashes) and the drive keeps a literal colon and forward slashes. Encoding either
# breaks parsing in a way that is easy to miss: file://C%3A%5Cx puts the drive letter in the HOST
# position, which the parser rejects as a foreign host, and file:///C%3A/x parses but yields a
# path with a leading slash ("/C:/git/x"). Both were measured against CwdReport.Parse.
__agterm_encode_path() {
  local path="$1" out="" i char
  for (( i = 0; i < ${#path}; i++ )); do
    char="${path:i:1}"
    case "$char" in
      [-_.~A-Za-z0-9/:]) out+="$char" ;;
      *) out+=$(printf '%%%02X' "'$char") ;;
    esac
  done
  printf '%s' "$out"
}

__agterm_report_cwd() {
  # A Windows-side path is what the spawning process can use as a working directory. Git Bash
  # exposes /c/... to the shell, so convert when cygpath is available and fall back to the
  # unix-shaped path otherwise (the parser accepts both shapes).
  local path
  if command -v cygpath >/dev/null 2>&1; then
    # -m yields forward slashes (C:/git/x) directly, which is the URI path shape.
    path=$(cygpath -m "$PWD" 2>/dev/null) || path="$PWD"
  else
    path="$PWD"
  fi
  # Three slashes: file:// + an empty authority + the absolute path.
  printf '\033]7;file:///%s\007' "$(__agterm_encode_path "${path#/}")"
}

# Append rather than assign: the user's rc (sourced above) may already own PROMPT_COMMAND, and
# clobbering it would break their prompt. Guarded so a re-source cannot register twice.
case "${PROMPT_COMMAND:-}" in
  *__agterm_report_cwd*) ;;
  "") PROMPT_COMMAND="__agterm_report_cwd" ;;
  *) PROMPT_COMMAND="${PROMPT_COMMAND%;};__agterm_report_cwd" ;;
esac

# The first prompt happens before PROMPT_COMMAND has run once, so report the starting directory
# now: otherwise a session that never cd's would keep reporting nothing at all.
__agterm_report_cwd
