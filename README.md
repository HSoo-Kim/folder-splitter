<img src="src/FolderSplitter/Assets/app.png" width="88" alt="Folder Splitter icon">

# Folder Splitter

**한국어** · [English](README.en.md) · [简体中文](README.zh-CN.md)

한 창에서 최대 **4개 폴더를 동시에 보고, 각 영역에서 독립적인 탭을 사용하는 Windows 파일 관리자**입니다.

Windows 기본 파일 목록(Shell `IExplorerBrowser`)을 사용합니다. 기존 `explorer.exe`를 수정하거나 교체하지 않으며, 별도 앱으로 실행됩니다.

## 예시 화면

![Folder Splitter의 4분할 화면과 영역별 독립 탭](docs/images/folder-splitter-overview.png)

실제 앱에서 샘플 폴더를 연 화면입니다. 각 영역의 탭과 주소창은 독립적으로 동작합니다.

## 실행

[최신 Windows 실행 파일 다운로드](https://github.com/HSoo-Kim/folder-splitter/releases/latest)

Windows 11 x64용입니다. 릴리스의 `folder-splitter-win-x64.zip`을 풀고 `FolderSplitter.exe`를 실행합니다. 배포 실행 파일에는 .NET 런타임이 포함되므로 별도 런타임 설치가 필요 없습니다.

초기 버전은 코드 서명되지 않았습니다. Windows가 다운로드한 앱의 게시자를 확인할 수 없다는 안내를 표시할 수 있습니다.

## 기능

- **1 / 2 / 3 / 4분할**: 상단에서 전환하며, 줄인 영역의 탭도 보존합니다. 3분할 메뉴에서 좌우 또는 상하 2:1 배치를 선택합니다.
- **탭 드래그 분할**: 탭을 파일 목록의 가장자리로 끌면 분할하고, 가운데로 끌면 해당 영역으로 이동합니다. 최대 4영역입니다. 탭 우클릭의 `Split tab`도 사용할 수 있습니다.
- **영역별 독립 탭**: 새 탭, 탭 복제, 닫은 탭 복원, 다른 영역으로 탭 이동.
- **개별 탐색**: 주소 입력, 경로 자동완성, 뒤로/앞으로/상위 폴더 이동.
- **분할 크기 조절**: 경계선을 드래그하고 `Equal size`로 균등 배치합니다.
- **빠른 접근과 즐겨찾기**: 사용자 폴더, 바탕화면, 다운로드, 문서, 내 PC, 휴지통. 원하는 폴더를 고정할 수 있습니다.
- **Windows 기본 파일 작업**: 파일 목록의 우클릭 메뉴, 복사/잘라내기/붙여넣기, 이름 변경, 삭제, 영역 간 드래그 앤 드롭.
- **보기 선택**: 각 영역의 옵션 메뉴에서 자세히, 목록, 아이콘 보기.
- **작업 복원**: 탭 경로, 선택 탭, 즐겨찾기, 분할 방식과 비율, 창 크기를 로컬에 저장합니다.
- **외부 탐색기 열기**: 영역 옵션에서 같은 폴더를 Windows 파일 탐색기로 엽니다.

파일 목록의 열 정렬과 우클릭 메뉴는 Windows Shell이 처리합니다. 기본 탐색기의 Windows 11 상단 명령 모음과 최신 탭 UI 자체를 임베드하는 방식은 아닙니다.

## 사용법

1. 상단 `4 panes`를 누릅니다.
2. 각 영역 주소창에 폴더 경로를 입력하고 Enter를 누릅니다.
3. 영역 오른쪽 `+`로 탭을 추가합니다.
4. 탭 우클릭으로 복제, 다른 영역으로 이동, 다른 탭 닫기를 사용합니다.
5. 파일 목록을 클릭한 뒤 Windows와 같은 단축키를 사용합니다.

왼쪽 바로가기는 현재 활성 영역에 열립니다. 활성 영역은 상단 파란 선으로 표시합니다. 즐겨찾기는 우클릭으로 제거할 수 있습니다.

`FAVORITES` 제목을 클릭하면 목록만 접거나 펼칩니다. 항목은 삭제되지 않으며 접힌 상태도 다음 실행에 복원됩니다.

### 단축키

| 키 | 동작 |
|---|---|
| `Ctrl+1`, `Ctrl+2`, `Ctrl+3`, `Ctrl+4` | 분할 방식 전환 |
| `F6` | 다음 영역으로 이동 |
| `Ctrl+T` | 현재 폴더를 새 탭으로 열기 |
| `Ctrl+W`, 탭의 ×, 탭 가운데 클릭 | 탭 닫기 |
| `Ctrl+Shift+T` | 닫은 탭 복원 |
| `Ctrl+Tab`, `Ctrl+Shift+Tab` | 다음/이전 탭 |
| `Ctrl+L`, `Alt+D` | 주소창 선택 |
| `Alt+Left`, `Alt+Right`, `Alt+Up` | 뒤로/앞으로/상위 폴더 |
| `Ctrl+D` | 현재 폴더 즐겨찾기 |
| `F5` | 새로고침 |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | 선택 파일 복사/잘라내기/붙여넣기 |
| `F2` | 선택 파일 이름 변경 |
| `Delete` | 선택 항목 삭제: Windows Shell 정책 적용 |
| `Ctrl+Shift+N` | 새 폴더 |
| `F1` | 단축키 도움말 |

## 데이터와 호환성

- 설정 위치: `%LOCALAPPDATA%\QuadExplorer\session.json`
- 이전 QuadExplorer 버전의 탭과 즐겨찾기를 유지하기 위해 설정 경로는 그대로 사용합니다.
- 같은 설정 파일을 사용하는 앱은 한 번에 하나만 실행합니다.
- 최대 영역당 32개 탭, 즐겨찾기 100개입니다.
- 연결이 끊긴 드라이브나 사라진 폴더는 오류를 표시하고 주소창으로 다른 폴더를 선택할 수 있습니다.
- 파일 삭제·덮어쓰기·충돌 처리는 Windows Shell에 위임합니다. 앱 자체가 사용자 파일을 재귀 삭제하지 않습니다.
- OneDrive/Dropbox 상태 표시, 제3자 Shell 확장, 네트워크 공유는 해당 공급자의 설치 상태와 권한에 영향을 받습니다.
- 현재 버전은 밝은 테마입니다. 전체 검색 인덱스, 기본 탐색기 교체, 기존 탐색기 탭 가져오기는 포함하지 않습니다.

별도 작업 세션이나 테스트용으로 설정 경로를 지정할 수 있습니다.

```powershell
.\FolderSplitter.exe --session C:\Work\folder-session.json C:\Projects D:\Research
```

추가 폴더 인수는 활성 영역의 새 탭으로 열립니다.

## 소스 빌드

필수: Windows 11 x64, .NET 10 SDK.

```powershell
.\build.ps1                 # Release 빌드 + 세션 회귀 테스트
.\build.ps1 -UiTests        # 실제 네이티브 창 통합 테스트 추가
.\build.ps1 -Publish        # 런타임 포함 실행 파일과 ZIP 생성
```

UI 통합 테스트는 로그인된 대화형 Windows 데스크톱에서 실행합니다. 테스트는 임시 폴더만 사용하며 탐색 완료 이벤트를 기다립니다. 고정 시간 지연에 의존하지 않습니다.

출력:

```text
artifacts/FolderSplitter/FolderSplitter.exe
artifacts/folder-splitter-win-x64.zip
```

## 구성

```text
src/FolderSplitter/
  MainForm.cs           창, 분할, 빠른 접근, 단축키
  ExplorerPane.cs       영역별 탭과 주소창
  ShellViewHost.cs      네이티브 Shell 보기 수명 관리
  ShellInterop.cs       Windows SDK COM/PInvoke 선언
  SessionState.cs       설정 검증과 원자적 저장
  Theme.cs              밝은 테마와 공통 컨트롤
  Assets/               앱 아이콘 원본 PNG와 다중 해상도 ICO
tests/
  QuadExplorer.Tests/   설정 모델 회귀 테스트
  QuadExplorer.UiTests/ 실제 Shell 보기 통합 테스트
```

앱 코드에는 외부 NuGet 패키지 의존성이 없습니다. Windows Shell과 .NET Windows Forms를 사용합니다.

아이콘 원본을 교체할 때는 아래 명령으로 Windows용 ICO를 다시 생성합니다.

```powershell
.\tools\Build-Icon.ps1 -SourcePng .\src\FolderSplitter\Assets\app.png
```
