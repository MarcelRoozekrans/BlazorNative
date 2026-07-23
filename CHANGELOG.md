# Changelog

## [0.5.1](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.5.0...v0.5.1) (2026-07-23)


### Bug Fixes

* close the default(T)-on-struct trap ([#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178), [#181](https://github.com/MarcelRoozekrans/BlazorNative/issues/181)) and guard NOT-API marks ([#196](https://github.com/MarcelRoozekrans/BlazorNative/issues/196)) ([061d10c](https://github.com/MarcelRoozekrans/BlazorNative/commit/061d10ca501b7739e9bd2e856adf1358ccc6ee32))
* surface geolocation accuracy on a separate trailing node ([#169](https://github.com/MarcelRoozekrans/BlazorNative/issues/169)) ([#195](https://github.com/MarcelRoozekrans/BlazorNative/issues/195)) ([37a89ca](https://github.com/MarcelRoozekrans/BlazorNative/commit/37a89ca75fab0a15675a7d5c6d123191f82c67b5))

## [0.5.0](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.4.1...v0.5.0) (2026-07-22)


### Features

* add the BnLog level-gated logging seam and migrate all 31 .NET call sites ([#185](https://github.com/MarcelRoozekrans/BlazorNative/issues/185)) ([b329851](https://github.com/MarcelRoozekrans/BlazorNative/commit/b3298517a550b78900937d7aafa4595b973bc6c6))
* pump the runtime's stderr into logcat and carry the log level from the shell ([#187](https://github.com/MarcelRoozekrans/BlazorNative/issues/187)) ([71868d0](https://github.com/MarcelRoozekrans/BlazorNative/commit/71868d051f222ae13f2a88b54c06a05f5fda005d))
* sweep the iOS shell's 78 NSLog sites onto an os_log seam (11.4 gate c) ([#188](https://github.com/MarcelRoozekrans/BlazorNative/issues/188)) ([71470db](https://github.com/MarcelRoozekrans/BlazorNative/commit/71470dbba75aeb5076297f7e4a0e0285f5a4d6bc))


### Bug Fixes

* abort the mount on a parameter-binding fault so [#164](https://github.com/MarcelRoozekrans/BlazorNative/issues/164) stops reporting rc 0 ([#189](https://github.com/MarcelRoozekrans/BlazorNative/issues/189)) ([f53f74a](https://github.com/MarcelRoozekrans/BlazorNative/commit/f53f74a52170011a789873da672c540962459f70))
* make the android stderr pump report a failed install honestly ([#191](https://github.com/MarcelRoozekrans/BlazorNative/issues/191)) ([#193](https://github.com/MarcelRoozekrans/BlazorNative/issues/193)) ([cf80930](https://github.com/MarcelRoozekrans/BlazorNative/commit/cf80930fb1f673bf7f5cbe2dd20a47e502b8c3b1))

## [0.4.1](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.4.0...v0.4.1) (2026-07-21)


### Bug Fixes

* make the ConfigureServices seam usable from the template's AppPages ([#165](https://github.com/MarcelRoozekrans/BlazorNative/issues/165)) ([a8a3273](https://github.com/MarcelRoozekrans/BlazorNative/commit/a8a327386284ad141558eb8e517b308c72aad2ac))

## [0.4.0](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.3.0...v0.4.0) (2026-07-20)


### Features

* add BlazorNativeApp.ConfigureServices app-service DI seam ([#159](https://github.com/MarcelRoozekrans/BlazorNative/issues/159)) ([1b880a6](https://github.com/MarcelRoozekrans/BlazorNative/commit/1b880a65c0c5b495b39befbf38ff8a986f6f93d5))

## [0.3.0](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.2.0...v0.3.0) (2026-07-20)


### Features

* **build:** ship RouteGen in the Runtime package so a generated app derives its route map (M11 [#1](https://github.com/MarcelRoozekrans/BlazorNative/issues/1)) ([#153](https://github.com/MarcelRoozekrans/BlazorNative/issues/153)) ([c4f6745](https://github.com/MarcelRoozekrans/BlazorNative/commit/c4f674588f233a858bdc2f0f72b491375bd6b07f))
* **routing:** generate the deep-link route map at build time, not by hand (M11 [#1](https://github.com/MarcelRoozekrans/BlazorNative/issues/1)) ([#151](https://github.com/MarcelRoozekrans/BlazorNative/issues/151)) ([0bcf8e8](https://github.com/MarcelRoozekrans/BlazorNative/commit/0bcf8e8e5ef9b1a4e913e5b0fd3fe552a8b79432))
* ship deep-link route codegen to dotnet new apps (m11 phase 11.0 gate b) ([c4f6745](https://github.com/MarcelRoozekrans/BlazorNative/commit/c4f674588f233a858bdc2f0f72b491375bd6b07f))

## [0.2.0](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.1.1...v0.2.0) (2026-07-20)


### Features

* **shells:** bundle inter and force one font on both shells for text parity ([#126](https://github.com/MarcelRoozekrans/BlazorNative/issues/126)) ([#146](https://github.com/MarcelRoozekrans/BlazorNative/issues/146)) ([50320bf](https://github.com/MarcelRoozekrans/BlazorNative/commit/50320bf60fd8ee0a6bb794456ce255212427926b))

## [0.1.1](https://github.com/MarcelRoozekrans/BlazorNative/compare/v0.1.0...v0.1.1) (2026-07-19)


### Bug Fixes

* **build:** govern Exports.VersionNumber ([#120](https://github.com/MarcelRoozekrans/BlazorNative/issues/120)) and drift-pin RuntimeFrameworkVersion ([#122](https://github.com/MarcelRoozekrans/BlazorNative/issues/122)) ([#143](https://github.com/MarcelRoozekrans/BlazorNative/issues/143)) ([3d3d374](https://github.com/MarcelRoozekrans/BlazorNative/commit/3d3d37427a88377154500d986e72ab93b04ace28))
* precision + grouped cleanups + docs accuracy sweep ([#124](https://github.com/MarcelRoozekrans/BlazorNative/issues/124), [#125](https://github.com/MarcelRoozekrans/BlazorNative/issues/125), [#119](https://github.com/MarcelRoozekrans/BlazorNative/issues/119)) ([#144](https://github.com/MarcelRoozekrans/BlazorNative/issues/144)) ([7ca1ac5](https://github.com/MarcelRoozekrans/BlazorNative/commit/7ca1ac5e9f819e3d4867bf6ef9030c7e4eda07a0))
* surface Frames subscriber faults ([#123](https://github.com/MarcelRoozekrans/BlazorNative/issues/123)) and report the real iOS platform kind ([#121](https://github.com/MarcelRoozekrans/BlazorNative/issues/121)) ([#141](https://github.com/MarcelRoozekrans/BlazorNative/issues/141)) ([068dbd7](https://github.com/MarcelRoozekrans/BlazorNative/commit/068dbd709dc3ca16918553773e003fedff72f07b))

## 0.1.0 (2026-07-18)


### Features

* **device:** biometrics + OS-key-bound secure storage — the 9.0 ABI's 3rd free reuse (M9 DoD [#4](https://github.com/MarcelRoozekrans/BlazorNative/issues/4)) ([#128](https://github.com/MarcelRoozekrans/BlazorNative/issues/128)) ([0bb3865](https://github.com/MarcelRoozekrans/BlazorNative/commit/0bb386575040c760bf04b4ba565ea8ff27125a5b))
* **device:** camera photo capture — the ABI stayed frozen a 4th time (M9 DoD [#5](https://github.com/MarcelRoozekrans/BlazorNative/issues/5)) ([#129](https://github.com/MarcelRoozekrans/BlazorNative/issues/129)) ([c8dcb28](https://github.com/MarcelRoozekrans/BlazorNative/commit/c8dcb28d94c429f34709b72e32240f391be45cf1))
* **host:** permission pattern + geolocation, the ABI's first grow since 3.1 (M9 DoD [#1](https://github.com/MarcelRoozekrans/BlazorNative/issues/1)+[#2](https://github.com/MarcelRoozekrans/BlazorNative/issues/2)) ([#118](https://github.com/MarcelRoozekrans/BlazorNative/issues/118)) ([3866410](https://github.com/MarcelRoozekrans/BlazorNative/commit/386641053915ebd4579ffaab5bdd6f2f99922ba4))
* **notifications:** local notifications + tap-through, the 9.0 ABI's first free reuse (M9 DoD [#3](https://github.com/MarcelRoozekrans/BlazorNative/issues/3)) ([#127](https://github.com/MarcelRoozekrans/BlazorNative/issues/127)) ([17e6834](https://github.com/MarcelRoozekrans/BlazorNative/commit/17e683430542d15a05155a1cbfebf16934e02d57))
* **release:** release-please authors the version; the owner keeps the click ([#108](https://github.com/MarcelRoozekrans/BlazorNative/issues/108)) ([0aeb69c](https://github.com/MarcelRoozekrans/BlazorNative/commit/0aeb69c5a6795070aacfa8f900dae5f706756831))
* **release:** the version becomes pre-1.0 semver; the first release will be 0.1.0 ([#114](https://github.com/MarcelRoozekrans/BlazorNative/issues/114)) ([7518ca6](https://github.com/MarcelRoozekrans/BlazorNative/commit/7518ca607a9cb5a215441f5ae49ac0899eea1c91))
