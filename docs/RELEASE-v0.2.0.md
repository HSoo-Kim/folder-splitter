# Folder Splitter 0.2.0

한 창에서 최대 4개 폴더를 동시에 보고, 각 영역에서 독립적인 탭을 사용하는 Windows 파일 관리자입니다.

## 다운로드 및 실행

1. 아래 첨부된 `folder-splitter-win-x64.zip`을 다운로드합니다.
2. 압축을 풀고 `FolderSplitter.exe`를 실행합니다.

Windows 11 x64용이며 .NET 런타임이 포함되어 있습니다. 별도 런타임 설치는 필요 없습니다.
현재 실행 파일은 코드 서명되지 않았습니다.

## 이번 릴리스

- 앱 이름을 **Folder Splitter**로 변경했습니다.
- 폴더와 4분할 창을 결합한 새 아이콘을 실행 파일과 창에 적용했습니다.
- 초기 네이티브 파일 보기의 드래그 앤 드롭 등록 문제를 수정했습니다.
- 한국어를 기본 README로 제공하며 영어와 중국어 간체 문서를 추가했습니다.
- 기존 탭과 즐겨찾기를 유지하기 위해 이전 설정 경로는 그대로 사용합니다.

## 주요 기능

- 1/2/4분할과 영역별 독립 탭·주소창.
- 탭 복제, 닫은 탭 복원, 다른 영역으로 탭 이동.
- 즐겨찾기, 분할 크기 조절, 작업 상태 복원.
- Windows Shell의 파일 작업과 우클릭 메뉴.

## 검증

설정 모델 12개와 네이티브 UI 검사 18개가 통과했습니다.
실제 복사·붙여넣기, 이름 변경, 탭 단축키와 사용자의 마우스 드래그 이동도 확인했습니다.
자세한 범위와 별도 검증하지 않은 환경은 `docs/VERIFICATION.md`에 기록했습니다.

## Documentation

Korean is the default README. English and Simplified Chinese READMEs are included in the ZIP and repository.
