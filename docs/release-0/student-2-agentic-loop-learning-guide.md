# Student 2 Agentic Loop Learning Guide

This guide explains the Student 2 workflow in simple terms. It is a technical
reference, not a personal reflection. Write assessment reflections in your own
words.

## The simple idea

Think of the workflow as a small software team:

1. **Gemini plans** what success should look like.
2. **Qwen proposes** how Student 2 could implement the plan.
3. **Automated checks observe** whether the code builds and the tests pass.
4. **Gemini reviews** the proposal and objective results.
5. **A human approves** which change may be applied.
6. **The checks run again** to verify the approved adaptation.

The formal name is:

`Plan -> Act -> Observe -> Adapt -> Verify`

## Why two models are used

### Gemini 3 Flash

Exact model ID: `gemini-3-flash-preview`

Gemini is used for **Plan** and **Observe** because it is fast at organising a
goal into acceptance criteria and reviewing a proposal at a high level.

Gemini receives:

- the goal;
- the focus area;
- a short approved project-context summary;
- Qwen's proposal;
- pass or fail results from deterministic checks.

Gemini does not receive Student 2 source code in this workflow. The approved
context prevents it from inventing unrelated business rules.

### Qwen3.8-27B

Exact Ollama model: `qwen3.8:27b`

Qwen is used for **Act** because it runs locally and can work with scoped Student
2 implementation context without sending that context to an external service.

The model is large, so a response may take several minutes. A short context file
is more reliable than supplying the entire repository.

## What each stage means

| Stage | Beginner meaning | Output |
|---|---|---|
| Plan | Decide what should be built and how success will be measured. | User journey, business rule, steps and acceptance criteria |
| Act | Propose the code and tests needed to implement the plan. | File-by-file proposal |
| Observe | Compare the proposal with objective build and test evidence. | Gemini review and check results |
| Adapt | Apply only a change that a human has approved. | Approved patch |
| Verify | Repeat the same checks after the change. | Post-change results |

## Deterministic checks

An AI response is an opinion. A deterministic check produces the same kind of
result every time it runs.

The script runs:

- `git diff --check`;
- the Student 2 NUnit tests;
- the Student 2 frontend build;
- a frontend output smoke check;
- `docker compose config --quiet`.

Passing tests prove that the code matches the expectations encoded in those
tests. They do not prove that the business requirements or user experience are
correct. This is called the **test-oracle problem**, which is why an independent
review stage is also used.

## Human approval

The script does not apply Qwen's suggestion automatically. To apply a patch, a
person must provide:

- the patch file;
- approval text explaining what was approved;
- the `-ApplyApprovedPatch` switch.

This is called a **human-in-the-loop control**.

## Running a review-only loop

From the repository root:

```powershell
.\scripts\agentic-loop\student-2-agentic-loop.ps1 `
    -Goal "Review prerequisite-gated Student enrolment." `
    -Focus implementation
```

This creates evidence but does not change source code.

## Running an approved adaptation

```powershell
.\scripts\agentic-loop\student-2-agentic-loop.ps1 `
    -Goal "Require prerequisite eligibility before enrolment." `
    -Focus implementation `
    -ApprovedPatchPath "C:\path\approved-change.patch" `
    -ApprovalText "Approved after reviewing the backend gate and tests." `
    -ApplyApprovedPatch
```

The script validates the patch before applying it and permits changes only under
`student-2` or `.github/workflows/student-2-ci.yml`.

## Evidence

Each run creates one JSON file under:

`docs\release-0\agentic-loop-evidence`

The JSON records:

- exact model identifiers;
- every prompt;
- raw Gemini and Qwen outputs;
- the local context supplied to Qwen;
- deterministic check output;
- human approval;
- the applied patch targets;
- verification results;
- branch and commit identifiers;
- whether the run failed.

API keys are never written to the evidence file.

## Current Release 0 limitation

The prerequisite page uses manually entered completed Courses and grades. The
backend recalculates whether that evidence satisfies the Course rules, but the
grades are not yet verified as official Academic Records data. This limitation
must be stated honestly in the assessment.

## Rules to remember

1. Use exact registered model identifiers in assessment evidence.
2. Keep source context small and relevant.
3. Never let an AI apply a patch without human approval.
4. Verify an adaptation with the same checks used before it.
5. Record failed attempts honestly instead of presenting them as successful.
