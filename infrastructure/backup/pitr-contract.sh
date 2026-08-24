#!/usr/bin/env bash

validate_archive_catalog() {
    local catalog=$1 timeline=$2 segment=$3
    jq -e --arg segment "$segment" --argjson timeline "$timeline" '
        type == "array"
        and any(.[];
            .id == $timeline
            and .status == "OK"
            and (.missing_segments | length) == 0
            and .start_segment <= $segment
            and .end_segment >= $segment)
    ' <<<"$catalog" >/dev/null 2>&1
}

validate_archive_deadline() {
    local seconds=$1
    [[ "$seconds" =~ ^[0-9]+$ ]] && (( seconds > 0 && seconds <= 3600 ))
}

validate_restore_isolation() {
    local source_path restore_path
    source_path=$(realpath -m "$1")
    restore_path=$(realpath -m "$2")
    [[ "$source_path" != "$restore_path" ]]
}

validate_source_unchanged() {
    [[ -n "$1" && "$1" == "$2" ]]
}
