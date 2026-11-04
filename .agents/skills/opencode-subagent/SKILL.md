---
name: opencode-subagent
description: >-
  Orchestrate and delegate programming, refactoring, unit test writing, and documentation tasks
  to the free OpenCode subagent powered by opencode/muse-spark-1.3-contributor-free (1M token context, 0$ cost).
  Use this skill when performing routine coding, writing test suites, generating boilerplates, or analyzing large files.
---

# Free OpenCode Subagent (Gemini Orchestrator Workflow)

This skill guides Antigravity (operating with Gemini 3.8 Flash) in acting as the lead architect and delegating implementation, repetitive coding, testing, and documentation to the free subagent.

## Model Profile
- **Model IDs:** `opencode/muse-spark-1.3-contributor-free`, `opencode/deepseek-v4.1-flash-free`
- **Context Limit:** 1,048,576 tokens (1M tokens)
- **Cost:** 100% Free (0$ input / 0$ output)
- **Reasoning Variants:** `minimal`, `low`, `medium`, `high`, `xhigh`
- **Specialties:** Boilerplate code, Unit tests, Docstrings, File refactoring, AST/Log parsing.

---

## Mandatory Rule: 3+ Subagents Parallel Swarm
- **Каждый раз при постановке пользователем ЛЮБОЙ задачи** оркестратор ОБЯЗАН декомпозировать её и запустить **не менее 3 субагентов параллельно**.
- Запрещено выполнять рутину последовательно или вручную.

---

## Delegation Workflow

### 1. Identify Candidate Tasks for Delegation
Delegate to the subagent when the task matches any of the following:
- Writing unit tests or mocks for existing functions/classes.
- Writing boilerplate classes, DTOs, schemas, or CRUD handlers.
- Documenting existing codebases (docstrings, markdown specs, READMEs).
- Refactoring small-to-medium files or functions according to a strict specification.
- Parsing or summarizing massive log files or code files (using its 1M token window).

Do **NOT** delegate:
- High-level architectural decisions and user interaction (stay as orchestrator in Gemini).
- Resolving conflicting business requirements or ambiguous user requests.

### 2. Execution Methods

#### Method A: Via MCP Tool (`delegate_task_to_subagent`)
If the MCP server is active, call `delegate_task_to_subagent` directly:
- `instruction`: Clear, specific instructions including required function signatures and constraints.
- `target_files`: Absolute or workspace-relative paths of files to be inspected or edited.
- `reasoning_effort`:
  - `low` or `medium` for simple boilerplates and tests.
  - `high` for non-trivial refactorings and algorithm implementations.
- `auto_apply_edits`: `True` to allow direct file modification.

#### Method B: Via CLI Runner (Fallback / Direct Execution)
Run via PowerShell/Terminal:
```powershell
python -m src.opencode_runner "Напиши юнит-тесты на pytest для файла src/service.py" -f src/service.py --variant medium
```

### 3. Orchestrator Quality Verification (Gemini Duty)
After the subagent finishes:
1. Review the generated/modified files.
2. Run project tests or lint commands (e.g. `pytest`, `npm test`, `ruff`).
3. Ensure no regressions or unwanted file removals occurred.
4. Present the verified result cleanly to the user.
