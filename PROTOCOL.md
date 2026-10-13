# PROTOCOL.md — Interface Control Document (ICD)

**문서명:** Telescope ↔ Observatory Monitor 통신 규격
**문서 버전:** 0.2 (초안 — 구현 착수용 보완판)
**상태:** Draft — 시뮬레이터·클라이언트 구현 전에 확정하고, 이후 변경은 이 문서를 먼저 고친 뒤 코드에 반영한다.

이 문서가 유일한 기준이다. `Telescope.Simulator`와 `Observatory.Monitor.Wpf`는 서로의 코드를 참조하지 않고 이 문서만 보고 구현한다.

---

## 1. 범위

| 항목 | 내용 |
|---|---|
| 대상 | 가상 천문 관측 장비(망원경 + 돔 + CCD 카메라 + 기상 센서)와 모니터링·제어 클라이언트 간 통신 |
| 역할 | 장비 측 = **Server** (Telescope.Simulator), 제어 측 = **Client** (Observatory.Monitor.Wpf) |
| 비범위 | 인증·암호화, 다중 클라이언트 동시 접속, 실제 하드웨어 드라이버 |

## 2. 전송 계층

| 항목 | 값 |
|---|---|
| 프로토콜 | TCP |
| 포트 | 5000 (시뮬레이터 옵션으로 변경 가능) |
| 접속 수 | 동시 1 클라이언트. 이미 접속 중이면 새 접속은 즉시 close |
| 인코딩 | ASCII (UTF-8 호환), 한글 등 비ASCII 금지 |
| 프레이밍 | **한 줄 = 한 메시지**, 종단 문자 `\n` (LF). 수신 측은 `\r`이 붙어 있으면 제거 |
| 최대 길이 | 1줄 256바이트 |

## 3. 메시지 형식

```
<TYPE>,<NAME>[,<RESULT>][,<KEY>=<VALUE>]...\n
```

- 필드 구분자는 `,` 이고 공백은 넣지 않는다.
- `KEY=VALUE` 필드는 **순서에 의존하지 않는다.** 수신 측은 모르는 키를 무시해야 한다 (하위 호환).
- 대소문자 구분. TYPE/NAME/KEY는 대문자.
- 숫자는 `.` 소수점의 10진수 (로케일 영향 금지: C#에서는 `CultureInfo.InvariantCulture` 사용).
- 형식 위반 줄을 받으면 무시하고 로그에 남긴다. 연결은 끊지 않는다.

### 3.1 메시지 종류

| TYPE | 방향 | 용도 |
|---|---|---|
| `DATA` | Server → Client | 주기 상태 송신 (텔레메트리) |
| `CMD` | Client → Server | 제어 명령 |
| `ACK` | Server → Client | 명령 응답 (성공/실패) |
| `EVT` | Server → Client | 비동기 이벤트 (완료, 인터록 등) |

## 4. DATA — 주기 텔레메트리

- 주기: **1초** (허용 오차 ±200 ms)
- 접속 직후부터 명령 없이도 계속 송신한다.

```
DATA,TEMP=-19.8,HUM=62,WIND=3.2,ALT=45.0,AZ=180.0,STATE=TRACKING,DOME=OPEN,ILK=0
```

| 키 | 타입 | 단위 | 범위 | 설명 |
|---|---|---|---|---|
| `TEMP` | float | °C | -60 ~ +40 | **CCD 온도** |
| `HUM` | int | %RH | 0 ~ 100 | 외부 습도 |
| `WIND` | float | m/s | 0 ~ 60 | 풍속 |
| `ALT` | float | deg | 0 ~ 90 | 망원경 고도각 |
| `AZ` | float | deg | 0 ≤ AZ < 360 | 망원경 방위각 |
| `STATE` | enum | – | 5.1 참조 | 장비 상태 |
| `DOME` | enum | – | `OPEN` `CLOSED` `MOVING` | 돔 슬릿 상태 |
| `ILK` | 0/1 | – | – | 안전 인터록 래치 여부 (6장) |

> `TEMP`~`AZ` 다섯 개가 기본 필드이고, `STATE`·`DOME`·`ILK`는 상태 표시와 인터록 확인용 확장 필드다.

**Heartbeat 규칙:** 클라이언트가 **3초간 `DATA`를 받지 못하면** 링크 단절로 간주한다 (7.3 Recovery). 시간 판단의 기준 시계는 10.2를 따른다.

## 5. 장비 상태

### 5.1 STATE

| 값 | 의미 |
|---|---|
| `IDLE` | 대기. 이동·추적 없음 |
| `SLEWING` | 지정 좌표로 이동 중 |
| `TRACKING` | 목표 추적 중 (관측 상태) |
| `FAULT` | 장비 이상. `RESET` 전까지 이동·추적 명령 거부 |

### 5.2 상태 전이

```
            SLEW                 도착
   IDLE ───────────► SLEWING ───────────► TRACKING
    ▲  ◄──────────────  │                     │
    │      ABORT        │                     │
    │◄──────────────────┴─────────────────────┘
    │                    ABORT / 인터록
    │
    │   (임의 상태에서 CCD 치명 온도 등 장비 이상)
    └──────────────  FAULT  ◄───────────────────
            RESET
```

| 현재 | 이벤트 | 다음 |
|---|---|---|
| `IDLE` | `CMD,SLEW` 수락 | `SLEWING` |
| `IDLE` | `CMD,TRACK` 수락 | `TRACKING` |
| `SLEWING` | 목표 도착 | `TRACKING` |
| `SLEWING` `TRACKING` | `CMD,ABORT` | `IDLE` |
| `SLEWING` `TRACKING` | 인터록 발동 | `IDLE` (+ 돔 닫기, `ILK=1`) |
| `IDLE` | 인터록 발동 | `IDLE` 유지 (+ 돔 닫기, `ILK=1`) |
| 임의 | 장비 이상 (`TEMP ≥ +5.0 °C`, 9.1) | `FAULT` |
| `FAULT` | `CMD,RESET` 수락 (`TEMP < 0.0 °C`일 때만. 아니면 `ERR,CODE=NOT_CLEARED`) | `IDLE` |

- `SLEWING` 중 새 `CMD,SLEW`는 `BUSY`로 거부한다. 좌표를 바꾸려면 `ABORT` 후 재명령.
- 이동 속도(초안): 시뮬레이터가 **최대 2 deg/s**로 선형 이동한다.
- `FAULT` 중 인터록이 발동하면 `FAULT`를 유지한 채 돔 닫기·`ILK=1`만 수행한다 (6장).
- `RESET`은 `FAULT`와 `ILK`를 **함께** 해제하려 시도한다. 둘 중 하나라도 회복 전이면 `NOT_CLEARED`로 거부하고 어느 것도 해제하지 않는다.

## 6. 안전 인터록

**인터록은 장비 측(Server)에서 수행한다.** 클라이언트가 죽거나 링크가 끊겨도 장비는 스스로 안전 상태로 간다는 실제 장비의 설계 원칙을 따른다.

| 조건 | 기본 한계값 (초안, 시뮬레이터 옵션으로 변경) |
|---|---|
| 습도 | `HUM ≥ 85` |
| 풍속 | `WIND ≥ 12.0` |

인터록은 **`STATE`와 무관하게** 발동한다 (`IDLE` 중에도 돔은 닫는다). 이미 `ILK=1`이면 재발동과 `EVT` 중복 송신은 하지 않는다.

발동 시 서버 동작 (순서대로):

1. 이동·추적 중이면 중단 → `STATE=IDLE` (`IDLE`이면 그대로, `FAULT`이면 `FAULT` 유지)
2. 돔 닫기 시작 (`DOME=MOVING` → `CLOSED`). 이미 `CLOSED`면 생략
3. `ILK=1` 래치
4. `EVT,INTERLOCK,ACTIVE,REASON=HUM|WIND` 송신

해제 조건: 습도·풍속이 한계 아래로 회복(기본 한계값 기준 `HUM < 85`, `WIND < 12.0`) **되고** 클라이언트가 `CMD,RESET`을 보낸 뒤에만 `ILK=0`. 회복 전에 `RESET`을 받으면 `ACK,RESET,ERR,CODE=NOT_CLEARED`로 거부한다. (자동 해제 금지 — 사람이 확인하고 풀어야 한다.)

`ILK=1` 동안 `SLEW`, `TRACK`, `OPEN_DOME`은 `ERR=INTERLOCK`으로 거부한다.

## 7. CMD / ACK — 명령·응답

### 7.1 명령 목록

| 명령 | 인자 | 설명 | 허용 상태 |
|---|---|---|---|
| `SLEW` | `ALT`, `AZ` | 지정 좌표로 이동 | `IDLE` |
| `TRACK` | – | 현재 좌표 추적 시작 | `IDLE` |
| `ABORT` | – | 이동·추적 중단 | `SLEWING` `TRACKING` |
| `OPEN_DOME` | – | 돔 열기 | `ILK=0` |
| `CLOSE_DOME` | – | 돔 닫기 | 항상 |
| `RESET` | – | `FAULT`/인터록 해제 (원인이 회복된 경우에만, 5.2·6장) | `FAULT` 또는 `ILK=1` |

인자 범위: `ALT` 0 ~ 90, `AZ` 0 ≤ AZ < 360. 범위 밖은 `OUT_OF_RANGE`.

### 7.2 형식

모든 `CMD`에는 클라이언트가 붙이는 **`ID`**(정수, 1부터 증가)가 있고, `ACK`는 같은 `ID`를 되돌려준다.

```
C→S  CMD,SLEW,ID=17,ALT=60,AZ=200
S→C  ACK,SLEW,OK,ID=17
```

실패 응답:

```
S→C  ACK,SLEW,ERR,ID=17,CODE=BUSY
```

| CODE | 의미 |
|---|---|
| `BUSY` | 현재 상태에서 수행 불가 (이동 중 등) |
| `INVALID_ARG` | 인자 누락·형식 오류 |
| `OUT_OF_RANGE` | 인자 범위 초과 |
| `INTERLOCK` | 인터록 래치 중 |
| `FAULT` | 장비 FAULT 상태 |
| `UNKNOWN_CMD` | 정의되지 않은 명령 |
| `NOT_CLEARED` | `RESET` 시 해제 조건 미회복 (5.2, 6장) |

**`ID`를 둔 이유:** 무응답 후 재전송할 때, 늦게 도착한 이전 시도의 `ACK`와 재전송의 `ACK`를 구분하기 위해서다.

### 7.3 Timeout · Retry · Recovery (클라이언트 규칙)

| 파라미터 | 기본값 |
|---|---|
| 응답 Timeout | 2000 ms |
| 최대 시도 횟수 | 3 (최초 1회 + 재시도 2회) |
| 재시도 시 `ID` | **동일 `ID` 재사용** |

**서버의 중복 처리 (idempotency):** 서버는 최근 처리한 `ID` 32개를 기억한다. 같은 `ID`의 `CMD`가 다시 오면 명령을 **다시 실행하지 않고** 저장해 둔 `ACK`를 재송신한다. 이 캐시는 **접속(세션) 단위**다. 접속이 끊기거나 새 접속이 들어오면 비운다. 재접속한 클라이언트가 `ID`를 1부터 다시 시작해도 이전 세션의 `ACK`가 잘못 재송신되지 않게 하기 위함이며, 재접속 직전에 보낸 명령의 실행 여부는 첫 `DATA`의 `STATE`로 확인한다 (아래 Recovery 4번).

**Recovery 절차:**

1. 3회 모두 응답이 없으면 명령을 `FAILED(TIMEOUT)`으로 기록하고 알람 `COMM_TIMEOUT`을 올린다.
2. 클라이언트는 `DATA`로 실제 `STATE`를 확인해 명령이 서버에서 실행됐는지 판단한다 (ACK만 유실된 경우 상태가 이미 바뀌어 있다).
3. 연속 3건이 실패하거나 `DATA`가 3초 이상 끊기면 링크 단절로 보고 재접속한다: 대기 1 → 2 → 4 → 8초, 이후 10초 간격.
4. 재접속 성공 후 첫 `DATA`로 상태를 동기화하고 알람 `LINK_RESTORED`를 기록한다.

### 7.4 명령 직렬화

클라이언트는 응답 대기 중 새 명령을 보내지 않는다 (동시에 in-flight 명령은 최대 1개). 대기 중 입력은 UI에서 비활성화하거나 큐에 쌓는다.

## 8. EVT — 비동기 이벤트

```
EVT,<NAME>,<STATE>[,KEY=VALUE]...
```

| 이벤트 | 예 | 발생 시점 |
|---|---|---|
| `SLEW` | `EVT,SLEW,DONE,ALT=60.0,AZ=200.0` | 이동 완료 |
| `INTERLOCK` | `EVT,INTERLOCK,ACTIVE,REASON=HUM` | 인터록 발동 |
| `INTERLOCK` | `EVT,INTERLOCK,CLEARED` | `RESET`으로 해제 |
| `FAULT` | `EVT,FAULT,ACTIVE,REASON=CCD_OVERTEMP` | FAULT 진입 |

`EVT`는 응답이 아니므로 `ACK` 없이 송신하며, 유실 시에도 다음 `DATA`의 `STATE`·`ILK`로 상태를 복구할 수 있어야 한다.

## 9. 알람 규격 (클라이언트 처리)

알람 판단은 클라이언트가 `DATA`를 받아 수행한다. 인터록(6장)과 달리 **알림·기록**이 목적이다.

### 9.1 임계치 알람

| 대상 | WARN | CRIT | 비고 |
|---|---|---|---|
| `TEMP` (CCD) | > -10 °C | > 0 °C | 상승 방향만 감시. 초안값 |
| `HUM` | ≥ 75 % | ≥ 85 % | CRIT 값은 인터록 한계와 동일 |
| `WIND` | ≥ 9.0 m/s | ≥ 12.0 m/s | CRIT 값은 인터록 한계와 동일 |

- 채터링 방지: 해제는 한계값에서 **히스테리시스**(온도 1 °C, 습도 3 %, 풍속 1 m/s)만큼 벗어났을 때.
- `TEMP`가 **+5.0 °C 이상**이면 장비가 `FAULT`로 진입한다 (5.2). 임계치 알람의 `CRIT`(> 0 °C)와는 별개이며, 클라이언트는 `FAULT` 진입도 알람(`FAULT`)으로 기록한다. 복구는 `TEMP < 0.0 °C`로 내려온 뒤 `CMD,RESET`으로만 가능하다.

### 9.2 통계 알람 (이상 감지)

- 대상: `TEMP`, `HUM`, `WIND` 각각.
- 방법: **직전 N=60개** 샘플(현재 샘플 제외)의 이동평균 μ와 표준편차 σ를 구해, **연속 2개 샘플**이 `|x − μ| > 3σ`이면 `WARN`(`STAT_3SIGMA`). 한 번 올린 알람은 `|x − μ| ≤ 3σ`로 돌아오면 해제한다.
- 워밍업: 샘플 30개가 쌓이기 전에는 판단하지 않는다.
- 관리도(control chart): μ, μ±3σ를 차트에 함께 그린다.
- 용도: 이 알람은 **스파이크·급변 검출용**이다. 완만한 선형 상승(예: CCD 온도 +0.02 °C/s)은 창 안 표준편차(약 0.35 °C)가 함께 커져 3σ(약 1.1 °C)를 넘지 못하므로, 그런 경우는 9.1의 임계치 알람이 담당한다. 시뮬레이터의 `spike` 명령(11.1)으로 이 알람을 재현한다.

### 9.3 통신 알람

`COMM_TIMEOUT`, `LINK_LOST`, `LINK_RESTORED` (7.3).

### 9.4 알람 이력 레코드

| 필드 | 설명 |
|---|---|
| `ts` | 발생 시각 (ISO 8601, 로컬 오프셋 포함) |
| `level` | `INFO` `WARN` `CRIT` |
| `code` | `TEMP_HIGH` `HUM_HIGH` `WIND_HIGH` `STAT_3SIGMA` `COMM_TIMEOUT` `LINK_LOST` `LINK_RESTORED` `INTERLOCK` `FAULT` |
| `value` | 발생 시 측정값 (해당 시) |
| `cleared_ts` | 해제 시각 (미해제 시 비움) |

## 10. 로그 · 리플레이 규격

### 10.1 로그 파일

- 위치·이름: `logs/yyyyMMdd-HHmmss.log`, 접속 시작 시 새 파일.
- **프로토콜 원문을 그대로** 저장한다 (파싱 결과가 아니라 송수신한 줄).

```
2026-10-03T21:14:05.123+09:00 RX DATA,TEMP=-19.8,HUM=62,WIND=3.2,ALT=45.0,AZ=180.0,STATE=TRACKING,DOME=OPEN,ILK=0
2026-10-03T21:14:06.410+09:00 TX CMD,SLEW,ID=17,ALT=60,AZ=200
2026-10-03T21:14:06.455+09:00 RX ACK,SLEW,OK,ID=17
2026-10-03T21:14:08.410+09:00 SYS TIMEOUT id=18 attempt=1
2026-10-03T21:14:20.002+09:00 SYS ALARM WARN TEMP_HIGH value=-9.8
```

형식: `<timestamp> <DIR> <payload>`; `DIR` = `RX` | `TX` | `SYS`.

### 10.2 리플레이

- 로그의 `RX` 줄만 읽어 **실제 수신 경로와 동일한 파서**에 넣는다. 화면·차트·알람이 라이브와 같은 코드로 동작해야 한다.
- 속도: 원본 타임스탬프 간격 기준 ×1 / ×5 / ×10.
- 리플레이 중에는 `TX`를 하지 않는다 (명령 전송 UI 비활성화).
- **시계 규칙:** 하트비트(3초), Timeout, 3σ 창처럼 시간에 의존하는 모든 판단은 **주입된 시계**를 기준으로 한다. 라이브에서는 시스템 시계, 리플레이에서는 로그 타임스탬프를 시계로 쓴다 (리플레이 중 실시간 하트비트가 오동작하지 않도록).

## 11. 시뮬레이터 동작 규격

### 11.1 결함 주입

| 동작 | 기본값 | 목적 |
|---|---|---|
| 명령 무응답 | 확률 **3 %** (`--drop-rate`), 유형 `--drop-mode ack\|cmd\|both` (기본 `both`) | Timeout·Retry 검증 |
| CCD 온도 상승 | +0.02 °C/s (`--ccd-drift`) | 알람 검증 |
| 시나리오 | `normal` / `storm` / `ccd-fault` (`--scenario`) | 인터록·FAULT 재현 |
| 난수 시드 | `--seed N` | 재현 가능한 테스트 |

- **무응답의 정의:** 서버가 `CMD`를 수신하고 **명령은 실행하되 `ACK`만 송신하지 않는다** (`ack`), 또는 수신 자체를 버린다 (`cmd`). 기본값 `both`는 두 유형을 50 %씩 무작위로 섞는다. 두 경우가 모두 발생해야 7.3의 중복 처리와 상태 확인 절차를 검증할 수 있고, 테스트에서는 `--drop-mode ack` 또는 `cmd`로 한 유형만 지정해 재현한다.
- 3 %만으로는 3회 연속 실패(약 0.003 %)가 사실상 나오지 않으므로, 런타임 콘솔 명령으로 Recovery 경로를 강제한다:

| 콘솔 입력 | 동작 |
|---|---|
| `drop <0~1> [ack\|cmd\|both]` | 무응답 확률(과 유형) 변경 (`drop 1.0` = 완전 무응답) |
| `hang <sec>` | 지정 시간 동안 모든 송신 중단 (링크 단절 재현) |
| `storm` | 습도·풍속을 한계 초과 방향으로 상승 |
| `fault` | CCD 온도를 `FAULT` 진입값(+5.0 °C) 이상으로 급상승 |
| `spike <TEMP\|HUM\|WIND> [n]` | 다음 n개(기본 2) `DATA`의 해당 값에 급변량을 가산해 송신 — 3σ 통계 알람(9.2) 재현. 가산량 초안: TEMP +3.0 °C, HUM +12 %RH, WIND +5.0 m/s. 내부 모델 상태는 바꾸지 않는다 |

### 11.2 시뮬레이션 모델 (초안)

- `TEMP`: 초기 -20.0 °C, 매초 `+drift` + 가우시안 노이즈(σ=0.1).
- `HUM`, `WIND`: 평균 회귀 랜덤워크 (초기 60 %, 3 m/s).
- `ALT`, `AZ`: `SLEWING` 중 최대 2 deg/s 선형 이동, 도착 시 목표값에 스냅. `AZ`는 최단 경로 회전.
- `TRACKING` 중 `AZ`는 항성 추적을 흉내 내 초당 0.004 deg 증가 (0.004 ≈ 15″/s).

## 12. ASCOM Alpaca 대응표 (참고)

이 프로토콜은 Alpaca(HTTP/REST)가 아닌 **TCP 라인 프로토콜**이다. 다만 개념·명명은 ASCOM 인터페이스를 참고했고, 나중에 Alpaca 어댑터를 얹을 수 있도록 대응 관계를 남긴다. 아래 매핑은 ASCOM 공식 인터페이스 문서와 대조해 확정할 것.

| 이 프로토콜 | ASCOM 인터페이스 | 대응 개념 |
|---|---|---|
| `DATA.ALT` / `AZ` | Telescope | `Altitude` / `Azimuth` |
| `STATE=SLEWING` | Telescope | `Slewing` |
| `CMD,SLEW` | Telescope | `SlewToAltAzAsync` |
| `CMD,ABORT` | Telescope | `AbortSlew` |
| `DATA.TEMP` | Camera | `CCDTemperature` |
| `DATA.HUM` / `WIND` | ObservingConditions | `Humidity` / `WindSpeed` |
| `DATA.DOME`, `CMD,OPEN_DOME`/`CLOSE_DOME` | Dome | `ShutterStatus`, `OpenShutter`/`CloseShutter` |
| `DATA.ILK` | SafetyMonitor | `IsSafe` (의미 반대: `ILK=1` ≈ `IsSafe=false`) |

## 13. 변경 이력

| 버전 | 날짜 | 내용 |
|---|---|---|
| 0.1 | 2026-09-28 | 초안 작성 |
| 0.2 | 2026-10-12 | 구현 착수 전 보완 — 인터록 `STATE` 무관 발동(6장), ID 캐시 세션 단위(7.3), `FAULT` 진입·복구 조건과 `NOT_CLEARED`(5.2·9.1), 3σ 계산 창·연속 2회·용도(9.2), 시계 주입 규칙(10.2), `--drop-mode`·`spike`(11.1) |

## 14. 미결정 사항 (구현 전 확정)

- [ ] 임계값 기본값 (9.1, 6장) — 도메인 자료 확인 후 조정
- [ ] `TRACKING` 도착 판정 허용 오차 (현재 정확히 스냅)
- [ ] `RESET` 시 돔 자동 열림 여부 (현재: 열지 않음, 별도 `OPEN_DOME` 필요)
- [ ] 로그 파일 rotation·용량 제한
- [ ] `FAULT` 진입값(+5.0 °C)·복구값(0.0 °C), `spike` 가산량 — 시뮬레이터 구현 후 조정
