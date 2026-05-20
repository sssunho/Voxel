# Unity 복셀 엔진

> Unity Job System + Burst Compiler 기반 고성능 복셀 렌더링 엔진

[![Unity](https://img.shields.io/badge/Unity-2022-black?logo=unity)](https://unity.com/)
[![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Burst](https://img.shields.io/badge/Burst_Compiler-enabled-blue)](https://docs.unity3d.com/Packages/com.unity.burst@latest)

---

## 데모

> *(플레이 영상 GIF 또는 스크린샷 삽입 권장)*

---

## 프로젝트 개요

마인크래프트 스타일의 복셀 월드를 렌더링하는 엔진입니다.  
단순 구현에서 출발해 **단계별 성능 측정과 병목 분석**을 반복하며 약 **25배의 렌더링 속도 개선**을 달성했습니다.

### 핵심 구현 목록

- **BitGreedyMesher** — 비트 연산 기반 그리디 메싱 알고리즘 직접 구현
- **청크 스트리밍** — 플레이어 위치 기반 동적 청크 로드/언로드
- **멀티스레드 메시 빌드** — Unity Job System + Burst Compiler 활용
- **ChunkRenderer 오브젝트 풀링** — GC 최소화를 위한 렌더러 재사용
- **인접 청크 패딩** — 청크 경계 메시를 정확히 처리하기 위한 패딩 구조

---

## 성능 최적화 기록

256 크기 맵(단일 블록 타입, Perlin noise) 기준 단계별 측정 결과입니다.

| 최적화 단계 | 총 실행 시간 | GC Alloc | Greedy Meshing | 비고 |
|---|---|---|---|---|
| 최초 구현 | 3,078ms | 65.2MB | 2,914ms | 기준점 |
| MeshInput 구조 도입 | 782ms | 48.1MB | 536ms | **3.9배 개선** |
| 불필요한 Debug.Log 제거 | 718ms | 36.7MB | 549ms | GC 감소 |
| Mask Build Job + Burst 적용 | 448ms | 208.2MB | 224ms | 속도 37% 개선 |
| 내부 버퍼 재사용 | **123ms** | **5.4MB** | **42ms** | **최종: 25배 개선** |

### 주요 최적화 포인트

**1. MeshInput 구조 도입 (3.9배 개선의 핵심)**

기존 방식은 메시 빌드마다 `VoxelWorld`를 통해 블록을 조회했습니다.  
좌표 변환, Dictionary 조회, 함수 호출 체인이 반복되어 캐시 효율이 낮았습니다.

```
기존: 메시 빌드 → VoxelWorld 조회 → Dictionary 탐색 → 블록 반환 (반복)
개선: 메시 빌드 전 → BlockType[] 단일 배열로 복사 → 캐시 친화적 순차 접근
```

`ChunkMeshInput.Blocks`는 `NativeArray<BlockType>`(byte형)으로 구성되어  
데이터 크기를 줄이고 순차 접근 패턴으로 캐시 히트율을 높였습니다.

**2. 비트 연산 그리디 메싱 (BitGreedyMesher)**

기존 그리디 메싱은 각 슬라이스를 순차 탐색했습니다.  
`ulong` 비트마스크를 도입해 64개 블록을 동시에 처리합니다.

```csharp
// 가시면 판별: 블록이 있고(col) 이웃이 없는(~(col >> 1)) 위치
ulong pVisible = col & ~(col >> 1);  // 양의 방향 가시면
ulong nVisible = col & ~(col << 1);  // 음의 방향 가시면

// 최하위 비트 추출로 O(1) 위치 탐색
int n = math.tzcnt(pVisible);
pVisible &= pVisible - 1;  // 처리한 비트 제거
```

**3. 내부 버퍼 재사용**

매 빌드마다 새 버퍼를 할당하는 대신 `NativeArray`를 재사용합니다.  
GC Alloc이 48MB → 5.4MB로 감소했고, 버퍼 할당/해제 오버헤드가 제거됐습니다.

---

## 시스템 구조

```
VoxelWorld                    (블록 데이터 저장, 청크 관리)
├── Chunk[]                   (NativeArray<Voxel> 기반 청크 데이터)
└── CreateChunkMeshInput()    (인접 청크 패딩 포함 메시 입력 생성)

ChunkStreamingManager         (플레이어 위치 기반 청크 로드/언로드 판단)
└── ChunkRendererManager      (렌더러 풀 관리, 리빌드 큐 처리)
    └── ChunkRenderer[]       (메시 빌드 및 렌더링)
        └── BitGreedyMesher   (메시 생성 알고리즘)
```

### 메시 빌드 파이프라인

```
CreateChunkMeshInput (Job)
  └─ 인접 청크 블록을 패딩 영역에 복사
  └─ NativeArray<BlockType> 출력

BitGreedyMesher.BuildMesh
  ├─ CreateAxisColJob (IJobFor, Burst)
  │   └─ X/Y/Z 축 방향으로 ulong 컬럼 비트마스크 생성
  ├─ CreateVisibleMaskJob (IJob, Burst)
  │   └─ 컬럼 비트 연산으로 가시면 마스크 계산
  └─ GreedyMeshJob × 6 (IJob, Burst)
      └─ 6방향 각각 그리디 메싱 → 버텍스/인덱스 출력
```

---

## 주요 코드 설명

### 인접 청크 패딩 처리 (`VoxelWorld.cs`)

청크 경계의 가시면을 올바르게 처리하기 위해 청크 크기 + 2의 패딩 배열을 사용합니다.  
인접 청크가 없는 경우 Air로 처리해 외벽 면이 렌더링되도록 합니다.

```csharp
public struct ChunkMeshInput : IDisposable
{
    public NativeArray<BlockType> Blocks; // (ChunkSize + 2)^3 크기
    // ...
}
```

### 청크 Dirty 시스템 (`VoxelWorld.cs`)

블록 변경 시 해당 청크뿐 아니라 경계에 인접한 청크도 Dirty 처리합니다.

```csharp
if (localPos.x == 0)
    SetChunkDirtyIfExist(new Vector3Int(chunkPos.x - 1, chunkPos.y, chunkPos.z));
// ... 6방향 처리
```

### ChunkRenderer 오브젝트 풀링 (`ChunkRendererManager.cs`)

청크 언로드 시 렌더러를 Destroy하지 않고 풀에 반환합니다.

```csharp
void PushRenderer(ChunkRenderer renderer)
{
    _deactivateRenderers.Add(renderer);
    renderer.SetVisible(false);
    renderer.ClearMesh();
}
```

---

## 파일 구조

```
├── VoxelWorld.cs              # 블록 데이터 저장, 청크 관리, MeshInput 생성
├── VoxelWorldBehaviour.cs     # VoxelWorld MonoBehaviour 래퍼
├── BitGreedyMesher.cs         # 비트 연산 기반 그리디 메싱 알고리즘
├── ChunkRenderer.cs           # 청크 메시 렌더링, PlaneDesc 정의
├── ChunkRendererManager.cs    # 렌더러 풀 관리, 리빌드 큐 처리
├── ChunkStreamingManager.cs   # 플레이어 기반 청크 로드/언로드
├── BlockInteractionController.cs  # 블록 설치/파괴 입력 처리
├── MapGenerator.cs            # 테스트 맵 생성 (Floor, Cube, PerlinNoise)
└── Voxel.cs                   # 블록 타입 정의, VoxelStatics
```

---

## 개발 환경

- Unity 2022.x
- Unity Jobs 0.51+
- Unity Burst 1.8+
- Unity Collections 1.4+
- Unity Mathematics 1.2+

---

## 관련 링크

- [게임플레이 영상 (차원소녀 Chrono Quest)](https://youtu.be/LZnPH_YcvDU)