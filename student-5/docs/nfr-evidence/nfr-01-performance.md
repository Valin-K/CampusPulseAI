# NFR-01 — Performance evidence

**Requirement:** Facility and equipment availability search must return within two seconds under normal local demonstration conditions.

**Method:** `curl -s -o /dev/null -w "%{time_total}s"` against the running `eventscheduling.backend` gateway (the same container the CI smoke test targets), five requests per endpoint, first request included (cold path) rather than discarded.

## Results

| Endpoint | Run 1 | Run 2 | Run 3 | Run 4 | Run 5 |
|---|---|---|---|---|---|
| `GET /api/rooms` | 0.292s | 0.068s | 0.005s | 0.005s | 0.007s |
| `GET /api/refequipment` | 0.048s | 0.006s | 0.005s | 0.005s | 0.005s |
| `POST /api/bookings` | 0.146s | 0.054s | 0.012s | 0.015s | 0.015s |

**Slowest observed:** 0.292s (`GET /api/rooms`, first request against a just-started container).
**Requirement:** ≤ 2.000s.
**Margin:** every observed request is at least 6.8x faster than the 2-second bound.

## Reproduce

```bash
docker compose -f docker-compose.yml -f .github/docker-compose.student-5.ci.yml up -d \
  academicrecords.database eventscheduling.database eventscheduling.backend

for i in 1 2 3 4 5; do
  curl -s -o /dev/null -w "GET /api/rooms: %{time_total}s\n" http://localhost:18085/api/rooms
done
```
