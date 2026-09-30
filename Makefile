# row_vr — dev loop front door.
#
#   Tier 1  make test              ~2s     no headset   stroke logic
#   Tier 2  Unity Play Mode        ~5s     no headset   scene, feel, UI
#   Tier 3  make dev               2-8min  headset      tracking, perf, comfort
#
# Run `make` with no target for this list.

SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help

# Pass-through knobs: `make build RELEASE=1`, `make connect IP=10.0.0.5`.
export RELEASE
export NOLOGS
export IP
export PORT
export CLIP
export NAME
export QUEST_CROP
export MAX_SIZE
export UNITY_PATH
export APP_ID

DOTNET_TEST_PROJ := tests/RowVR.Stroke.Tests/RowVR.Stroke.Tests.csproj

.PHONY: help preflight connect disconnect bootstrap build deploy dev logs cast \
        test test-unity preview record record-stop pull-recordings promote clean distclean

help: ## Show this help
	@echo "row_vr dev loop"
	@echo
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}'
	@echo
	@echo "  Tier 1 is 'make test' and needs neither Unity nor a headset. Prefer it."

preflight: ## Check adb, Unity, JDK, udev rules and device authorisation
	@tools/preflight.sh

connect: ## Switch the headset to wireless adb (needs one USB setup pass)
	@tools/quest-connect.sh

disconnect: ## Drop the wireless adb connection
	@adb disconnect || true

bootstrap: ## Apply Quest project settings from code (run after first opening Unity)
	@tools/bootstrap.sh

build: ## Tier 3a: build an IL2CPP/arm64 APK (RELEASE=1 for a release build)
	@tools/build.sh

deploy: ## Tier 3b: install, launch and follow logs (NOLOGS=1 to skip logs)
	@tools/deploy.sh

dev: ## Tier 3: build + deploy + logs. The full device loop.
	@tools/dev.sh

logs: ## Follow the app's logs (the MQDH log-viewer replacement)
	@tools/logs.sh

cast: ## Mirror the headset view to your desktop via scrcpy
	@tools/cast.sh

test: ## Tier 1: stroke detector tests. No Unity, no licence, no headset.
	@dotnet test $(DOTNET_TEST_PROJ) --nologo -v quiet

test-unity: ## Run Unity EditMode tests (slower; needs the Editor)
	@tools/test-unity.sh

preview: ## Tier 2, headless: render Play Mode stills to .devloop/preview (PREVIEW_SECONDS=16)
	@tools/preview.sh

record: ## Start a recording take on the headset (NAME=steady-22spm)
	@tools/record.sh start

record-stop: ## Stop the current take; the clip is written on the headset
	@tools/record.sh stop

pull-recordings: ## Pull hand-motion clips off the headset
	@tools/pull-recordings.sh

promote: ## Promote a raw clip to a committed test fixture (CLIP=... [NAME=...])
	@tools/promote-recording.sh

clean: ## Remove build output and loop state
	@rm -rf Build .devloop
	@echo "removed Build/ and .devloop/"

distclean: clean ## Also remove Unity's Library cache (forces a full reimport)
	@rm -rf Library Temp Logs obj
	@echo "removed Library/, Temp/, Logs/ — next Unity open will reimport everything"
