/* A deliberately silent installer-test process. It never loads an audio
   component, connects to an application pipe, or reads user settings. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

int wmain(int argc, wchar_t **argv) {
    if (argc != 2) return 1;
    HANDLE event = OpenEventW(SYNCHRONIZE, FALSE, argv[1]);
    if (!event) return 2;
    DWORD result = WaitForSingleObject(event, 120000);
    CloseHandle(event);
    return result == WAIT_OBJECT_0 ? 0 : 3;
}
