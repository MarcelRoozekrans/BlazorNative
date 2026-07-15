# BlazorNative — Developer Makefile
# ──────────────────────────────────────────────────────────────────────────────
# make devloop         → native fast lane: watch .NET src → publish → PreviewHost
# make devloop-android → device lane: publish → installDebug → launch → logcat
# make inspect         → DevTools inspector: native session + localhost page
# make runtime-publish → NativeAOT publish BlazorNative.Runtime for all 3 RIDs
# make android         → build the Android APK (BlazorNative.Jni via Gradle)
# make android-test    → boot AVD if needed + run connectedAndroidTest
# make test            → run all tests
# make clean           → clean all build artifacts
# ──────────────────────────────────────────────────────────────────────────────

DOTNET            := dotnet
RUNTIME_PROJECT   := src/BlazorNative.Runtime
ANALYZER_PROJECT  := src/BlazorNative.Analyzers/BlazorNative.Analyzers.csproj

.PHONY: devloop devloop-android inspect runtime-publish android android-build android-test android-test-visible test test-watch clean setup analyzers help

## ── Development ──────────────────────────────────────────────────────────────

devloop:                      ## Native fast lane: watch .NET src → publish win-x64 → PreviewHost tree (fast-restart)
	powershell -ExecutionPolicy Bypass -File scripts/devloop.ps1

devloop-android:              ## Device lane: publish bionic-x64 → installDebug → launch → logcat boot marker
	powershell -ExecutionPolicy Bypass -File scripts/devloop.ps1 -Android

inspect:                      ## DevTools inspector: native session + page on http://localhost:5199 (PORT=n, COMPONENT=Name to override)
	@echo "🔍 Starting BlazorNative Inspector (native session, fast-restart)..."
	cd src/BlazorNative.Jni && JAVA_HOME="C:/Program Files/Eclipse Adoptium/jdk-21.0.11.10-hotspot" ANDROID_HOME="$$USERPROFILE/AppData/Local/Android/Sdk" ./gradlew.bat runInspectorHost $(if $(PORT),-Pport=$(PORT),) $(if $(COMPONENT),-Pcomponent=$(COMPONENT),)

analyzers:                    ## Build Roslyn analyzers
	$(DOTNET) build $(ANALYZER_PROJECT) -c Release

runtime-publish:              ## NativeAOT publish BlazorNative.Runtime (win-x64 + both bionic ABIs)
	$(DOTNET) publish $(RUNTIME_PROJECT) -c Release -r win-x64
	$(DOTNET) publish $(RUNTIME_PROJECT) -c Release -r linux-bionic-x64
	$(DOTNET) publish $(RUNTIME_PROJECT) -c Release -r linux-bionic-arm64

## ── Mobile targets ───────────────────────────────────────────────────────────

android-build:                ## Build the Phase 2.2 Android APK (BlazorNative.Jni)
	@echo "🚀 Building Android APK (assembleDebug)..."
	cd src/BlazorNative.Jni && JAVA_HOME="C:/Program Files/Eclipse Adoptium/jdk-21.0.11.10-hotspot" ANDROID_HOME="$$USERPROFILE/AppData/Local/Android/Sdk" ./gradlew.bat assembleDebug --no-daemon

android-test:                 ## Boot AVD if needed + run connectedAndroidTest
	powershell -ExecutionPolicy Bypass -File scripts/test-android.ps1

android-test-visible:         ## Same as android-test but with visible emulator window
	powershell -ExecutionPolicy Bypass -File scripts/test-android.ps1 -ShowEmulator

android:                      ## Legacy alias — same as android-build
	@$(MAKE) android-build

## ── Testing ──────────────────────────────────────────────────────────────────

test:                         ## Run all tests
	$(DOTNET) test --logger "console;verbosity=normal"

test-watch:                   ## Run tests with file watcher
	$(DOTNET) watch test

## ── Utilities ────────────────────────────────────────────────────────────────

setup:                        ## Install required prerequisites
ifeq ($(OS),Windows_NT)
	@echo "📦 On Windows, run the canonical installer (admin shell):"
	@echo "    powershell -ExecutionPolicy Bypass -File setup.ps1"
	@echo ""
	@echo "It installs .NET 10 SDK, Temurin JDK 21, Android SDK + NDK 26.3,"
	@echo "and verifies the bionic NativeAOT publish toolchain."
else
	@echo "📦 setup.ps1 is Windows-only. On other platforms install manually:"
	@echo "  • .NET 10 SDK (feature band 10.0.3xx — see global.json)"
	@echo "  • Temurin JDK 21"
	@echo "  • Android SDK + NDK 26.3.11579264 + an x86_64 API-34 AVD"
endif

clean:                        ## Clean all build artifacts
	$(DOTNET) clean
	rm -rf artifacts
	find . -name "bin" -o -name "obj" | xargs rm -rf

help:                         ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*##' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*##"}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'
