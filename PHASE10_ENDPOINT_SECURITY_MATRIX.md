# Phase 10 Endpoint Security Matrix

Status values in the final column describe executed evidence. `Unit` means a direct service/controller test; `HTTP` means a real test-host request.

| Endpoint | Method | Authentication required | Allowed roles | Ownership/organization rule | CSRF required | Rate limiter | Audit event | HTTP integration test |
|---|---|---|---|---|---|---|---|---|
| `/api/auth/csrf` | GET | No | Any | None | No | No | None | Yes (`Real_http_cookie_login_and_csrf_matrix_is_enforced`) |
| `/api/auth/register` | POST | No | Patient created | Public registration creates PATIENT only | Yes | auth | None | No |
| `/api/auth/login` | POST | No | Any | Generic failure response | Yes | auth | LOGIN_SUCCESS / LOGIN_FAILURE | Yes (`Real_http_auth_errors...`, cookie login) |
| `/api/auth/logout` | POST | Yes | Any | Current session | Yes | No | LOGOUT | Yes (`Real_http_cookie_login_and_csrf_matrix_is_enforced`) |
| `/api/auth/me` | GET | Yes | Any | Current user only | No | No | None | No |
| `/api/reports/upload` | POST | Yes | LAB_STAFF, PATHOLOGIST | Organization claim assigned to upload | Yes | upload | REPORT_UPLOAD | No |
| `/api/reports/{id}` | GET | Yes | Any authenticated | Owned patient, uploader, organization, or active grant | No | No | REPORT_VIEW / ACCESS_DENIED | Yes (`Real_http_patient_report_ownership...`, grants, legacy) |
| `/api/reports/{id}/results` | GET | Yes | Any authenticated | Same report rule | No | No | None | Yes (`Real_http_resource_matrix...`) |
| `/api/reports/{id}/file` | GET | Yes | Any authenticated | Same report rule | No | No | PDF_VIEW / ACCESS_DENIED | Yes (`Real_http_resource_matrix...`) |
| `/api/reports/{reportId}/results/{resultId}/verify` | POST | Yes | LAB_STAFF, PATHOLOGIST | Authorized report modification | Yes | No | RESULT_VERIFY / ACCESS_DENIED | No |
| `/api/reports/{reportId}/results/{resultId}` | PATCH | Yes | LAB_STAFF, PATHOLOGIST | Authorized report modification | Yes | No | RESULT_CORRECT / ACCESS_DENIED | No |
| `/api/reports/{reportId}/results/{resultId}/audit` | GET | Yes | Any authenticated | Authorized report view | No | No | ACCESS_DENIED | No |
| `/api/reports/{reportId}/explanations/*` | POST | Yes | Any authenticated | Authorized report view | Yes | explanation | EXPLANATION_GENERATE / ACCESS_DENIED | Yes (`Real_http_resource_matrix...`, rate-limit test) |
| `/api/timeline` | GET | Yes | Any authenticated | Claims-scoped patient/organization reports | No | No | Not wired | No |
| `/api/timeline/{id}` | GET | Yes | Any authenticated | Claims-scoped report | No | No | Not wired | Yes (`Real_http_resource_matrix...`) |
| `/api/timeline/latest-results` | GET | Yes | Any authenticated | Claims-scoped results | No | No | Not wired | No |
| `/api/timeline/tests/{name}` | GET | Yes | Any authenticated | Claims-scoped results | No | No | Not wired | No |
| `/api/trends/tests` | GET | Yes | Any authenticated | Claims-scoped reports | No | No | Not wired | No |
| `/api/trends/{name}` | GET | Yes | Any authenticated | Claims-scoped reports | No | No | Not wired | No |
| `/api/trends/compare` | POST | Yes | Any authenticated | Claims-scoped report IDs | Yes | No | Not wired | Yes (`Real_http_resource_matrix...`) |

## Evidence summary

- Direct security tests remain in `tests/api/Phase10SecurityTests.cs`.
- HTTP integration security tests: 8 passed in `tests/api/Phase10HttpSecurityTests.cs` using `WebApplicationFactory<Program>` and an isolated SQLite in-memory database.
- Unlisted or `No` HTTP cells remain unverified; this matrix does not infer coverage from direct controller tests.
