#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import os
import sys
import shutil
import urllib.request
import zipfile
import subprocess

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
BUILD_DIR = os.path.join(BASE_DIR, "apk_build_env")
TOOLS_DIR = os.path.join(BUILD_DIR, "tools")
OUTPUT_APK = os.path.join(BASE_DIR, "PortDefenderTester.apk")

os.makedirs(TOOLS_DIR, exist_ok=True)

JAVA_BIN = r"C:\Users\Gms\.vscode\extensions\redhat.java-1.58.0-win32-x64\jre\25.0.4.1-win32-x86_64\bin"
JAVAC_EXE = os.path.join(JAVA_BIN, "javac.exe")
JAVA_EXE = os.path.join(JAVA_BIN, "java.exe")
KEYTOOL_EXE = os.path.join(JAVA_BIN, "keytool.exe")
JARSIGNER_EXE = os.path.join(JAVA_BIN, "jarsigner.exe")

AAPT2_JAR_URL = "https://dl.google.com/dl/android/maven2/com/android/tools/build/aapt2/8.2.2-10154469/aapt2-8.2.2-10154469-windows.jar"
R8_JAR_URL = "https://dl.google.com/dl/android/maven2/com/android/tools/r8/8.2.33/r8-8.2.33.jar"
ANDROID_JAR_URL = "https://raw.githubusercontent.com/Sable/android-platforms/master/android-33/android.jar"

def download_file(url, target_path, desc):
    if os.path.exists(target_path) and os.path.getsize(target_path) > 1000:
        print(f"[OK] {desc} уже загружен: {target_path}")
        return
    print(f"[...] Загрузка {desc} ({url})...")
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    with urllib.request.urlopen(req, timeout=30) as resp, open(target_path, 'wb') as f:
        shutil.copyfileobj(resp, f)
    print(f"[OK] {desc} успешно сохранён ({os.path.getsize(target_path)} байт)")

def main():
    print("=== Старт процесса сборки PortDefenderTester.apk ===")

    # 1. Загрузка aapt2
    aapt2_jar = os.path.join(TOOLS_DIR, "aapt2.jar")
    aapt2_exe = os.path.join(TOOLS_DIR, "aapt2.exe")
    download_file(AAPT2_JAR_URL, aapt2_jar, "aapt2.jar")
    if not os.path.exists(aapt2_exe):
        with zipfile.ZipFile(aapt2_jar, 'r') as z:
            z.extract("aapt2.exe", TOOLS_DIR)
        print("[OK] aapt2.exe извлечён")

    # 2. Загрузка r8
    r8_jar = os.path.join(TOOLS_DIR, "r8.jar")
    download_file(R8_JAR_URL, r8_jar, "r8.jar")

    # 3. Загрузка android.jar
    android_jar = os.path.join(TOOLS_DIR, "android.jar")
    download_file(ANDROID_JAR_URL, android_jar, "android.jar (API 33)")

    # 4. Компиляция ресурсов aapt2 compile
    res_dir = os.path.join(BASE_DIR, "PortDefenderMobileAndroid", "app", "src", "main", "res")
    compiled_res_zip = os.path.join(BUILD_DIR, "compiled_res.zip")
    print("[...] Компиляция ресурсов приложения (aapt2 compile)...")
    res_compile_cmd = [
        aapt2_exe, "compile", "--dir", res_dir, "-o", compiled_res_zip
    ]
    subprocess.check_call(res_compile_cmd)
    print("[OK] Ресурсы скомпилированы в compiled_res.zip")

    # 5. Линковка ресурсов aapt2 link
    manifest_file = os.path.join(BASE_DIR, "PortDefenderMobileAndroid", "app", "src", "main", "AndroidManifest.xml")
    gen_dir = os.path.join(BUILD_DIR, "gen")
    os.makedirs(gen_dir, exist_ok=True)
    unaligned_apk = os.path.join(BUILD_DIR, "unaligned.apk")

    print("[...] Линковка манифеста и ресурсов (aapt2 link)...")
    link_cmd = [
        aapt2_exe, "link",
        "-I", android_jar,
        "--manifest", manifest_file,
        "--java", gen_dir,
        "-o", unaligned_apk,
        "--auto-add-overlay",
        compiled_res_zip
    ]
    subprocess.check_call(link_cmd)
    print(f"[OK] Сформирован unaligned.apk и класс R.java в {gen_dir}")

    # 6. Компиляция Java классов через javac
    obj_dir = os.path.join(BUILD_DIR, "obj")
    os.makedirs(obj_dir, exist_ok=True)
    java_src = os.path.join(BASE_DIR, "PortDefenderMobileAndroid", "app", "src", "main", "java", "com", "startupmonitor", "portdefendertester", "MainActivity.java")
    r_java = os.path.join(gen_dir, "com", "startupmonitor", "portdefendertester", "R.java")

    print("[...] Компиляция исходных Java файлов (javac)...")
    javac_cmd = [
        JAVAC_EXE,
        "-cp", android_jar,
        "-d", obj_dir,
        "-source", "8",
        "-target", "8",
        r_java,
        java_src
    ]
    subprocess.check_call(javac_cmd)
    print("[OK] Байткод .class успешно скомпилирован")

    # 7. Конвертация в Dalvik байткод (DEX) через d8/r8
    dex_dir = os.path.join(BUILD_DIR, "dex")
    os.makedirs(dex_dir, exist_ok=True)
    class_files = []
    for root, _, files in os.walk(obj_dir):
        for f in files:
            if f.endswith(".class"):
                class_files.append(os.path.join(root, f))

    print(f"[...] Генерация classes.dex через D8 ({len(class_files)} классов)...")
    d8_cmd = [
        JAVA_EXE,
        "-cp", r8_jar,
        "com.android.tools.r8.D8",
        "--min-api", "24",
        "--output", dex_dir,
        "--lib", android_jar
    ] + class_files
    subprocess.check_call(d8_cmd)

    classes_dex = os.path.join(dex_dir, "classes.dex")
    print(f"[OK] classes.dex сформирован ({os.path.getsize(classes_dex)} байт)")

    # 8. Упаковка classes.dex в APK
    print("[...] Упаковка classes.dex в APK...")
    with zipfile.ZipFile(unaligned_apk, 'a') as z:
        z.write(classes_dex, "classes.dex")
    print("[OK] classes.dex внедрён в APK")

    # 9. Генерация debug keystore
    keystore_file = os.path.join(BUILD_DIR, "debug.keystore")
    if not os.path.exists(keystore_file):
        print("[...] Создание тестового сертификата debug.keystore...")
        keytool_cmd = [
            KEYTOOL_EXE,
            "-genkeypair",
            "-v",
            "-keystore", keystore_file,
            "-storepass", "android",
            "-alias", "androiddebugkey",
            "-keypass", "android",
            "-keyalg", "RSA",
            "-keysize", "2048",
            "-validity", "10000",
            "-dname", "CN=Android Debug,O=Android,C=US"
        ]
        subprocess.check_call(keytool_cmd)
        print("[OK] debug.keystore создан")

    # 10. Подпись APK через jarsigner
    print("[...] Подписание APK цифровой подписью (jarsigner)...")
    jarsigner_cmd = [
        JARSIGNER_EXE,
        "-sigalg", "SHA256withRSA",
        "-digestalg", "SHA-256",
        "-keystore", keystore_file,
        "-storepass", "android",
        "-keypass", "android",
        unaligned_apk,
        "androiddebugkey"
    ]
    subprocess.check_call(jarsigner_cmd)
    print("[OK] APK успешно подписан")

    # 11. Финальное копирование
    shutil.copy2(unaligned_apk, OUTPUT_APK)
    apk_size_kb = os.path.getsize(OUTPUT_APK) / 1024
    print("=" * 60)
    print(" СБОРКА УСПЕШНО ЗАВЕРШЕНА!")
    print(f"Итоговый файл APK: {OUTPUT_APK} ({apk_size_kb:.1f} KB)")
    print("=" * 60)

if __name__ == "__main__":
    main()
