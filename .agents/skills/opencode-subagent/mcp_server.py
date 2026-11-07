# -*- coding: utf-8 -*-
"""
MCP Server for Antigravity: Free OpenCode Subagent Integration.
Active Model: opencode/deepseek-v4.1-flash-free
Enables Antigravity (Gemini Orchestrator) to delegate subtasks to free OpenCode subagents.
OpenRouter is completely excluded and forbidden.
"""

import os
import sys
import json
from typing import List, Optional
from mcp.server.mcpserver import MCPServer

# Ensure project root is in sys.path
CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(CURRENT_DIR)
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

from src.opencode_runner import run_subagent_task, DEFAULT_MODEL, find_opencode_binary


server = MCPServer("opencode-subagent")


@server.tool()
def delegate_task_to_subagent(
    instruction: str,
    target_files: Optional[List[str]] = None,
    model: Optional[str] = None,
    reasoning_effort: str = "medium",
    auto_apply_edits: bool = True,
    timeout_seconds: int = 600,
    working_directory: Optional[str] = None,
) -> str:
    """
    Delegates a coding, testing, refactoring, or documentation task to the free OpenCode subagent.
    Verified active models: opencode/deepseek-v4.1-flash-free, opencode/beam-free,
    opencode/nemotron-3.5-lightning-free, opencode/nemotron-3.5-super-vl-free, opencode/glm-5.3-flash-free.

    :param instruction: Detailed task prompt describing what needs to be done.
    :param target_files: Optional list of relevant files (paths) to attach to the subagent's context.
    :param model: Specific free model ID from OpenCode swarm. If omitted, uses DEFAULT_MODEL.
    :param reasoning_effort: Reasoning effort level: 'minimal', 'low', 'medium', 'high', 'xhigh'. Default is 'medium'.
    :param auto_apply_edits: If True, allows the subagent to apply file modifications directly. Default is True.
    :param timeout_seconds: Timeout for execution in seconds. Default is 600.
    :param working_directory: Target working directory. If omitted, uses current workspace.
    :return: Formatted string containing execution outcome, subagent output, and metrics.
    """
    work_dir = working_directory or PROJECT_ROOT
    target_model = model or DEFAULT_MODEL

    result = run_subagent_task(
        prompt=instruction,
        model=target_model,
        work_dir=work_dir,
        files=target_files,
        variant=reasoning_effort,
        auto_approve=auto_apply_edits,
        timeout_seconds=timeout_seconds,
    )

    status_str = "SUCCESS" if result.success else f"FAILED (Exit Code {result.exit_code})"
    
    output_lines = [
        f"=== OpenCode Subagent Execution Report ===",
        f"Status: {status_str}",
        f"Model: {result.model} (Free Tier - 0$ cost)",
        f"Duration: {result.duration_seconds:.2f}s",
        f"Reasoning Effort: {reasoning_effort}",
        f"Working Directory: {work_dir}",
    ]

    if target_files:
        output_lines.append(f"Target Files: {', '.join(target_files)}")
    output_lines.append(f"Command executed: {result.command}")

    output_lines.append("\n--- Subagent Output ---")
    if result.stdout.strip():
        output_lines.append(result.stdout.strip())
    else:
        output_lines.append("(No stdout output captured)")

    if result.stderr.strip() and not result.success:
        output_lines.append("\n--- Stderr / Diagnostic Info ---")
        output_lines.append(result.stderr.strip())

    return "\n".join(output_lines)


@server.tool()
def get_subagent_info() -> str:
    """
    Returns information about the configured free subagent, model capabilities, and binary path.
    """
    opencode_bin = find_opencode_binary()
    info = {
        "active_model": DEFAULT_MODEL,
        "model_name": "DeepSeek V4.1 Flash Free (Verified Online)",
        "provider": "opencode (Zen API)",
        "pricing": {
            "input": "$0.00 / 1M tokens",
            "output": "$0.00 / 1M tokens",
            "is_free": True
        },
        "limits": {
            "context_window_tokens": 131072,
            "max_output_tokens": 32768
        },
        "capabilities": [
            "Tool Calling / File Operations",
            "Reasoning (variants: minimal, low, medium, high, xhigh)",
            "Verified live response time: ~13.5s",
            "Automated Code Generation & Tests",
            "Multi-agent swarm consensus"
        ],
        "opencode_binary": opencode_bin,
        "workspace_root": PROJECT_ROOT
    }
    return json.dumps(info, indent=2, ensure_ascii=False)


@server.tool()
def list_available_free_models() -> str:
    """
    Returns the catalog of all verified online free models provided by OpenCode Zen API for subagent tasks.
    """
    models = [
        {
            "id": "opencode/deepseek-v4.1-flash-free",
            "name": "DeepSeek V4.1 Flash Free (Verified Online)",
            "context": "131,072 tokens",
            "latency": "~13.5s",
            "features": "Flagship logic, architecture, bug hunting, C#/Win32"
        },
        {
            "id": "opencode/beam-free",
            "name": "Beam Free (Verified Online)",
            "context": "131,072 tokens",
            "latency": "~15.3s",
            "features": "Fast unit tests, scripting, refactoring"
        },
        {
            "id": "opencode/nemotron-3.5-lightning-free",
            "name": "Nemotron 3.5 Lightning Free (Verified Online)",
            "context": "262,144 tokens",
            "latency": "~14.7s",
            "features": "NVIDIA 262K, concurrency, thread safety analysis"
        },
        {
            "id": "opencode/nemotron-3.5-super-vl-free",
            "name": "Nemotron 3.5 Super VL Free (Verified Online)",
            "context": "1,000,000 tokens",
            "latency": "~14.5s",
            "features": "1M context, repository audit"
        },
        {
            "id": "opencode/glm-5.3-flash-free",
            "name": "GLM 5.3 Flash Free (Verified Online)",
            "context": "1,000,000 tokens",
            "latency": "~16.4s",
            "features": "1M context, full codebase analysis"
        },
        {
            "id": "opencode/flint-40b-free",
            "name": "Flint 40B Free (Verified Online)",
            "context": "131,072 tokens",
            "latency": "~18.8s",
            "features": "40B parameter dense model"
        }
    ]
    return json.dumps(models, indent=2, ensure_ascii=False)


if __name__ == "__main__":
    server.run()
