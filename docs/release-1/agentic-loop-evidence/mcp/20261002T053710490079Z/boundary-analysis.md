# MCP Tool Boundary Analysis

## Live Lab 07 Tool Executions

| Tool | Owner | Declared responsibility | Validation arguments | Result |
|---|---|---|---|---|
| `student_count` | Academic Records | Return the number of student profiles in Academic Records. | `{}` | PASS |
| `students_by_subject` | Academic Records | Return the number of students enrolled in a course or subject. | `{"subject_code": "CS101"}` | PASS |
| `project_files` | Repository | List files in the CampusPulseAI repository root or a safe child directory. | `{"directory_path": "."}` | PASS |
| `ci_report` | Repository | Read the latest local agentic-loop evidence report. | `{}` | PASS |

## Decision

The collector invokes the four Lab 07 tools using documented, bounded validation arguments. Other registered CampusPulseAI tools are outside this MCP integration run. Each result is checked against the live tool's expected response contract before it is supplied to the implementation and review models.
