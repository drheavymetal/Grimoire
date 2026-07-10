#!/usr/bin/env bash
#
# audit.sh — Grimoire quality gate.
#
# Enforces Pedro's mandate: no "espacios sin implementar o cosas solo
# estéticas sin implementar" — no unimplemented gaps, no aesthetic-only
# shells. See docs/REVIEW.md for what this script can and CANNOT verify.
#
# Usage:
#   scripts/audit.sh            # all checks, including builds/tests (slow)
#   scripts/audit.sh --fast     # static checks only (1-6), no builds
#   scripts/audit.sh --strict   # a SKIPPED gate counts as failure
#
# Exit codes:
#   0  clean
#   1  violations found (or, with --strict, something was skipped)
#   2  bad usage
#
# Escape hatch: a line carrying "audit-ok: <reason>" is exempt from
# checks 1, 2, 5 and 6 — but every exemption is printed in the summary,
# so it can never hide. Check 4 (core/ purity) has NO escape hatch:
# it is Invariant 6 of CLAUDE.md and only a new DECISIONS.md entry may
# relax it.
#
# Note: this uses bash (arrays, pipefail), not strict POSIX sh.

set -euo pipefail

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Prefer GNU grep: on some dev machines `grep` resolves to ugrep.
GREP=grep
case "$($GREP --version 2>/dev/null | head -n1)" in
    *"GNU grep"*) : ;;
    *) if [ -x /usr/bin/grep ]; then GREP=/usr/bin/grep; fi ;;
esac

FAST=0
STRICT=0
for arg in "$@"; do
    case "$arg" in
        --fast)   FAST=1 ;;
        --strict) STRICT=1 ;;
        -h|--help)
            sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "audit.sh: unknown argument '$arg' (use --fast, --strict or --help)" >&2
            exit 2
            ;;
    esac
done

VIOLATIONS=0
SKIPPED=0
ALLOWED=0

EXC=(
    --exclude-dir=node_modules
    --exclude-dir=bin
    --exclude-dir=obj
    --exclude-dir=dist
    --exclude-dir=.git
    --exclude-dir=coverage
    --exclude-dir=.turbo
)

header() {
    printf '\n=== %s ===\n' "$*"
}

# Each violation line already carries path:line; this adds the count + prefix.
violate_block() {
    # $1 = advice line, stdin = "path:line: message" lines
    local advice="$1" line n=0
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        printf 'VIOLATION  %s\n' "$line"
        n=$((n + 1))
    done
    if [ "$n" -gt 0 ]; then
        printf '           -> %s\n' "$advice"
        VIOLATIONS=$((VIOLATIONS + n))
    fi
    return 0
}

allow_block() {
    # stdin = "path:line: content" lines carrying audit-ok
    local line n=0
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        printf 'AUDIT-OK   %s\n' "$line"
        n=$((n + 1))
    done
    if [ "$n" -gt 0 ]; then
        ALLOWED=$((ALLOWED + n))
    fi
    return 0
}

skip() {
    printf 'SKIPPED    %s\n' "$*"
    SKIPPED=$((SKIPPED + 1))
}

ok() {
    printf 'OK         %s\n' "$*"
}

# ---------------------------------------------------------------------------
# Check 1 — forbidden markers in src/**
# Protects against: work left as a note instead of code. A TODO merged to
# main is an unimplemented gap wearing a label.
# ---------------------------------------------------------------------------
check_markers() {
    header "1/7 Forbidden markers in src/ (TODO, FIXME, HACK, XXX, NotImplementedException, throw new NotSupportedException)"
    if [ ! -d src ]; then
        skip "check 1: src/ does not exist yet"
        return 0
    fi
    local out
    out="$($GREP -rnI "${EXC[@]}" -E \
        -e '\b(TODO|FIXME|HACK|XXX)\b' \
        -e '\bNotImplementedException\b' \
        -e 'throw[[:space:]]+new[[:space:]]+NotSupportedException' \
        src/ 2>/dev/null || true)"
    if [ -z "$out" ]; then
        ok "no forbidden markers"
        return 0
    fi
    allow_block < <(printf '%s\n' "$out" | { $GREP 'audit-ok:' || true; })
    violate_block \
        "Implement it now, or delete it. If intentional (e.g. abstract base member), append '// audit-ok: <reason>' on the same line." \
        < <(printf '%s\n' "$out" | { $GREP -v 'audit-ok:' || true; })
}

# ---------------------------------------------------------------------------
# Check 2 — empty catch blocks in C#
# Protects against: exceptions swallowed silently, which turn broken features
# into features that "look fine" — the exact hollow shell we are hunting.
# Flags catch blocks whose body is empty or contains only comments.
# ---------------------------------------------------------------------------
check_empty_catch() {
    header "2/7 Empty/comment-only catch blocks in C#"
    if [ ! -d src ]; then
        skip "check 2: src/ does not exist yet"
        return 0
    fi
    local files out
    files="$(find src -name '*.cs' \
        -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/node_modules/*' \
        2>/dev/null || true)"
    if [ -z "$files" ]; then
        ok "no C# files yet"
        return 0
    fi
    # shellcheck disable=SC2086
    out="$(printf '%s\n' "$files" | xargs -r perl -e '
        local $/;
        for my $f (@ARGV) {
            open my $fh, "<", $f or next;
            my $s = <$fh>;
            close $fh;
            while ($s =~ /catch\s*(?:\([^)]*\)\s*)?(?:when\s*\([^)]*\)\s*)?\{([^{}]*)\}/gs) {
                my $body = $1;
                my $line = 1 + (substr($s, 0, $-[0]) =~ tr/\n//);
                my $b = $body;
                $b =~ s{//[^\n]*}{}g;
                $b =~ s{/\*.*?\*/}{}gs;
                next unless $b =~ /^\s*$/;
                if ($body =~ /audit-ok:/) {
                    print "A $f:$line: empty catch (audit-ok)\n";
                } else {
                    print "V $f:$line: catch block swallows the exception (body is empty or comments only)\n";
                }
            }
        }' 2>/dev/null || true)"
    if [ -z "$out" ]; then
        ok "no swallowed exceptions"
        return 0
    fi
    allow_block < <(printf '%s\n' "$out" | { $GREP '^A ' || true; } | sed 's/^A //')
    violate_block \
        "Handle it, log it (Serilog), or rethrow. If swallowing is genuinely correct, put '// audit-ok: <reason>' inside the block." \
        < <(printf '%s\n' "$out" | { $GREP '^V ' || true; } | sed 's/^V //')
}

# ---------------------------------------------------------------------------
# Check 3 — console.log left in the front
# Protects against: debug leftovers shipping. console.error / console.warn
# are allowed.
# ---------------------------------------------------------------------------
check_console_log() {
    header "3/7 console.log in src/front/src/"
    if [ ! -d src/front/src ]; then
        skip "check 3: src/front/src/ does not exist yet"
        return 0
    fi
    local out
    out="$($GREP -rnI "${EXC[@]}" --include='*.ts' --include='*.tsx' \
        -E '\bconsole\.log[[:space:]]*\(' src/front/src/ 2>/dev/null | { $GREP -v 'audit-ok:' || true; } || true)"
    if [ -z "$out" ]; then
        ok "no console.log"
        return 0
    fi
    violate_block \
        "Remove it, or use console.error/console.warn for real diagnostics." \
        < <(printf '%s\n' "$out")
}

# ---------------------------------------------------------------------------
# Check 4 — Invariant 6: src/front/src/core/ must be DOM-free
# Protects against: silently making the React Native port expensive.
# core/ may not reference window/document/localStorage/sessionStorage/
# navigator, nor import from ui/ or platform/. NO audit-ok escape here:
# relaxing this invariant requires a new entry in docs/DECISIONS.md.
# ---------------------------------------------------------------------------
check_core_purity() {
    header "4/7 Invariant 6: core/ is DOM-free and does not import ui/ or platform/"
    if [ ! -d src/front/src/core ]; then
        skip "check 4: src/front/src/core/ does not exist yet"
        return 0
    fi
    local globals imports
    globals="$($GREP -rnI "${EXC[@]}" --include='*.ts' --include='*.tsx' \
        -E '\b(window|document|localStorage|sessionStorage|navigator)\b' \
        src/front/src/core/ 2>/dev/null || true)"
    imports="$($GREP -rnI "${EXC[@]}" --include='*.ts' --include='*.tsx' \
        -E "(from|import)[[:space:]]*\(?[[:space:]]*['\"][^'\"]*[/.](ui|platform)(/[^'\"]*)?['\"]" \
        src/front/src/core/ 2>/dev/null || true)"
    if [ -z "$globals" ] && [ -z "$imports" ]; then
        ok "core/ is clean"
        return 0
    fi
    violate_block \
        "core/ must receive adapters via context (D12), never touch browser globals. Move this to platform/ or ui/. No audit-ok escape: changing this needs a DECISIONS.md entry." \
        < <(printf '%s\n' "$globals")
    violate_block \
        "core/ may not import from ui/ or platform/. Invert the dependency: pass an adapter in. No audit-ok escape." \
        < <(printf '%s\n' "$imports")
}

# ---------------------------------------------------------------------------
# Check 5 — aesthetic-only components (HEURISTIC — see docs/REVIEW.md)
# Protects against: components that look finished but are furniture.
# 5a: arrays of 3+ human-readable (multi-word) string literals hardcoded in
#     ui/ — the classic mock-data shell.
# 5b: page/view/screen/route components under ui/ that import nothing from
#     core/ — a page not wired to any data or logic is probably a facade.
# This is NOT sound static analysis. It has false positives and negatives;
# a human/LLM reviewer is still required (docs/REVIEW.md).
# ---------------------------------------------------------------------------
check_aesthetic_shells() {
    header "5/7 Aesthetic-only components in src/front/src/ui/ (heuristic)"
    if [ ! -d src/front/src/ui ]; then
        skip "check 5: src/front/src/ui/ does not exist yet"
        return 0
    fi
    local before=$VIOLATIONS

    # 5a — hardcoded content arrays
    local files out
    files="$(find src/front/src/ui -type f \( -name '*.tsx' -o -name '*.ts' \) \
        -not -path '*/node_modules/*' 2>/dev/null || true)"
    if [ -n "$files" ]; then
        out="$(printf '%s\n' "$files" | xargs -r perl -e '
            local $/;
            for my $f (@ARGV) {
                open my $fh, "<", $f or next;
                my $s = <$fh>;
                close $fh;
                while ($s =~ /\[([^][]*)\]/gs) {
                    my $body = $1;
                    my $line = 1 + (substr($s, 0, $-[0]) =~ tr/\n//);
                    my $n = 0;
                    $n += () = $body =~ /"[^"\n]*[A-Za-z][^"\n]* [^"\n]*[A-Za-z][^"\n]*"/g;
                    $n += () = $body =~ /'"'"'[^'"'"'\n]*[A-Za-z][^'"'"'\n]* [^'"'"'\n]*[A-Za-z][^'"'"'\n]*'"'"'/g;
                    next if $n < 3;
                    if ($body =~ /audit-ok:/) {
                        print "A $f:$line: hardcoded content array ($n multi-word strings, audit-ok)\n";
                    } else {
                        print "V $f:$line: hardcoded content array ($n multi-word strings) — mock data shell?\n";
                    }
                }
            }' 2>/dev/null || true)"
        allow_block < <(printf '%s\n' "$out" | { $GREP '^A ' || true; } | sed 's/^A //')
        violate_block \
            "Source this from a core/ hook (API data) or i18next. If it is legitimately static (e.g. a config constant), add '// audit-ok: <reason>' inside the array." \
            < <(printf '%s\n' "$out" | { $GREP '^V ' || true; } | sed 's/^V //')
    fi

    # 5b — pages/views that import nothing from core/
    local pages page hits=0
    pages="$(find src/front/src/ui -type f \( \
            -ipath '*/pages/*' -o -ipath '*/views/*' -o -ipath '*/screens/*' \
            -o -ipath '*/routes/*' -o -iname '*page.tsx' -o -iname '*view.tsx' \
            -o -iname '*screen.tsx' -o -iname '*route.tsx' \) -name '*.tsx' \
        -not -path '*/node_modules/*' 2>/dev/null || true)"
    if [ -n "$pages" ]; then
        while IFS= read -r page; do
            [ -n "$page" ] || continue
            if $GREP -qE "from[[:space:]]+['\"][^'\"]*core" "$page"; then
                continue
            fi
            if $GREP -q 'audit-ok:' "$page"; then
                printf 'AUDIT-OK   %s:1: page imports nothing from core/ (audit-ok in file)\n' "$page"
                ALLOWED=$((ALLOWED + 1))
                continue
            fi
            printf 'VIOLATION  %s:1: page/view component imports nothing from core/ — likely an aesthetic shell\n' "$page"
            hits=$((hits + 1))
        done <<< "$pages"
        if [ "$hits" -gt 0 ]; then
            printf '           -> Wire the page to a core/ hook. If it is genuinely static (legal page, about page), add a comment with '"'"'audit-ok: <reason>'"'"' near the top.\n'
            VIOLATIONS=$((VIOLATIONS + hits))
        fi
    fi

    if [ "$VIOLATIONS" -eq "$before" ]; then
        ok "no aesthetic shells detected (heuristic — a reviewer must still check wiring, see docs/REVIEW.md)"
    fi
}

# ---------------------------------------------------------------------------
# Check 6 — hardcoded UI strings bypassing i18next (HEURISTIC)
# Protects against: Invariant 7 (i18n es/en from the first commit).
# Flags JSX text nodes of 3+ words, and common text attributes
# (placeholder/title/alt/aria-label/label) with 3+ words, not routed
# through t(...). Single-line only; see docs/REVIEW.md for blind spots.
# ---------------------------------------------------------------------------
check_i18n() {
    header "6/7 Hardcoded UI strings bypassing i18next (heuristic)"
    if [ ! -d src/front/src ]; then
        skip "check 6: src/front/src/ does not exist yet"
        return 0
    fi
    local files out
    files="$(find src/front/src -type f -name '*.tsx' \
        -not -path '*/node_modules/*' 2>/dev/null || true)"
    if [ -z "$files" ]; then
        ok "no .tsx files yet"
        return 0
    fi
    out="$(printf '%s\n' "$files" | xargs -r perl -ne '
        next if /audit-ok:/;
        my $ln = $.;
        close ARGV if eof;
        # JSX text nodes: >  free text  <
        while (/>\s*([^<>{}]*[A-Za-z][^<>{}]*?)\s*</g) {
            my $t = $1;
            my @w = grep { /[A-Za-z]{2,}/ } split /\s+/, $t;
            next if @w < 3;
            print "$ARGV:$ln: JSX text node not wrapped in t(...): \"$t\"\n";
            last;
        }
        # Text-bearing attributes with hardcoded values
        while (/\b(placeholder|title|alt|aria-label|label)\s*=\s*"([^"]*)"/g) {
            my ($attr, $v) = ($1, $2);
            my @w = grep { /[A-Za-z]{2,}/ } split /\s+/, $v;
            next if @w < 3;
            print "$ARGV:$ln: hardcoded $attr=\"$v\" — route through t(...)\n";
        }
    ' 2>/dev/null || true)"
    if [ -z "$out" ]; then
        ok "no hardcoded UI strings found (heuristic — multi-line text nodes are not seen)"
        return 0
    fi
    violate_block \
        "Move the string to the i18next catalogs (es/en, English keys) and render {t('...')}. If it must stay literal, append '// audit-ok: <reason>'." \
        < <(printf '%s\n' "$out")
}

# ---------------------------------------------------------------------------
# Check 7 — build and test gates (slow; skipped with --fast)
# Protects against: code that does not even compile, tests that do not run,
# lint rules (curly: all) not enforced. Each gate reports separately.
# ---------------------------------------------------------------------------
run_gate() {
    # $1 = gate name, $2 = working dir, rest = command
    local name="$1" dir="$2"
    shift 2
    local log
    log="$(mktemp "${TMPDIR:-/tmp}/audit-gate.XXXXXX")"
    if (cd "$dir" && "$@") >"$log" 2>&1; then
        ok "gate '$name' passed"
    else
        VIOLATIONS=$((VIOLATIONS + 1))
        printf 'VIOLATION  gate %s FAILED — command: %s (in %s). Last 40 lines:\n' "$name" "$*" "$dir"
        tail -n 40 "$log" | sed 's/^/    | /'
        printf '           -> Fix the errors above; the gate must pass before commit.\n'
    fi
    rm -f "$log"
}

check_gates() {
    header "7/7 Build and test gates"
    if [ "$FAST" -eq 1 ]; then
        printf '(--fast: build/test gates not run)\n'
        return 0
    fi

    # .NET
    if [ -f src/web/Grimoire.slnx ]; then
        run_gate "dotnet-build" . dotnet build src/web/Grimoire.slnx -warnaserror --nologo
        run_gate "dotnet-test" . dotnet test src/web/Grimoire.slnx --nologo
    else
        skip "gate dotnet-build: src/web/Grimoire.slnx does not exist yet"
        skip "gate dotnet-test: src/web/Grimoire.slnx does not exist yet"
    fi

    # Front
    if [ -f src/front/package.json ]; then
        if [ ! -d src/front/node_modules ]; then
            skip "gates pnpm-lint/pnpm-build: src/front/node_modules missing — run 'pnpm install' in src/front first"
            skip "gate pnpm-build: (same cause)"
        else
            if $GREP -q '"lint"[[:space:]]*:' src/front/package.json; then
                run_gate "pnpm-lint" src/front pnpm lint
            else
                skip "gate pnpm-lint: no 'lint' script in src/front/package.json — add one (eslint with curly: all is a repo convention)"
            fi
            if $GREP -q '"build"[[:space:]]*:' src/front/package.json; then
                run_gate "pnpm-build" src/front pnpm build
            else
                skip "gate pnpm-build: no 'build' script in src/front/package.json"
            fi
        fi
    else
        skip "gate pnpm-lint: src/front/package.json does not exist yet"
        skip "gate pnpm-build: src/front/package.json does not exist yet"
    fi
}

# ---------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------

printf 'Grimoire audit — root: %s\n' "$ROOT"
if [ "$FAST" -eq 1 ]; then printf 'mode: --fast (no builds)\n'; fi
if [ "$STRICT" -eq 1 ]; then printf 'mode: --strict (skips are failures)\n'; fi

check_markers
check_empty_catch
check_console_log
check_core_purity
check_aesthetic_shells
check_i18n
check_gates

printf '\n================ AUDIT SUMMARY ================\n'
printf 'Violations        : %d\n' "$VIOLATIONS"
printf 'Skipped checks    : %d%s\n' "$SKIPPED" "$([ "$STRICT" -eq 1 ] && echo '  (strict: these count as failures)' || echo '')"
printf 'audit-ok in force : %d  (listed above as AUDIT-OK — verify each reason still holds)\n' "$ALLOWED"

RESULT=0
if [ "$VIOLATIONS" -gt 0 ]; then
    RESULT=1
fi
if [ "$STRICT" -eq 1 ] && [ "$SKIPPED" -gt 0 ]; then
    RESULT=1
fi

if [ "$RESULT" -eq 0 ]; then
    printf 'RESULT: PASS\n'
else
    printf 'RESULT: FAIL — do not commit until this passes. See docs/REVIEW.md.\n'
fi
exit "$RESULT"
