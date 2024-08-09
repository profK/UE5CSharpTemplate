﻿#include "UnrealSharpEditor.h"

#include "AssetToolsModule.h"
#include "DirectoryWatcherModule.h"
#include "IDirectoryWatcher.h"
#include "CSharpForUE/CSManager.h"
#include "CSharpForUE/CSDeveloperSettings.h"
#include "Misc/ScopedSlowTask.h"
#include "Reinstancing/CSReinstancer.h"
#include "UnrealSharpProcHelper/CSProcHelper.h"

#define LOCTEXT_NAMESPACE "FUnrealSharpEditorModule"

void FUnrealSharpEditorModule::StartupModule()
{
	FCSManager& Manager = FCSManager::Get();
	if (!Manager.IsInitialized())
	{
		Manager.OnUnrealSharpInitializedEvent().AddRaw(this, &FUnrealSharpEditorModule::OnUnrealSharpInitialized);
	}
	else
	{
		OnUnrealSharpInitialized();
	}
}

void FUnrealSharpEditorModule::ShutdownModule()
{
	FTSTicker::GetCoreTicker().RemoveTicker(TickDelegateHandle);
	UToolMenus::UnRegisterStartupCallback(this);
	UToolMenus::UnregisterOwner(this);
}

void FUnrealSharpEditorModule::OnCSharpCodeModified(const TArray<FFileChangeData>& ChangedFiles)
{
	if (bIsReloading)
	{
		return;
	}
	const UCSDeveloperSettings* Settings = GetDefault<UCSDeveloperSettings>();

	for (const FFileChangeData& ChangedFile : ChangedFiles)
	{
		// Skip generated files in bin and obj folders
		if (ChangedFile.Filename.Contains("Script/bin") || ChangedFile.Filename.Contains("Script/obj"))
		{
			continue;
		}

		// Check if the file is a .cs file
		FString Extension = FPaths::GetExtension(ChangedFile.Filename);
		if (Extension != "cs")
		{
			continue;
		}
		
		// Return on the first .cs file we encounter so we can reload.
		bIsReloading = true;

		// Return early and let TickDelegate handle Reload
		if (Settings->bRequireFocusForHotReload)
		{
			return;
		}
		
		StartHotReload();
		bIsReloading = false;
		return;
	}
}

void FUnrealSharpEditorModule::StartHotReload()
{
	FScopedSlowTask Progress(4, LOCTEXT("ReloadingCSharp", "Building C# code..."));
	Progress.MakeDialog();

	// Build the user's project.
	if (!FCSProcHelper::InvokeUnrealSharpBuildTool(Build))
	{
		return;
	}

	// Weave the user's project.
	Progress.EnterProgressFrame(1, LOCTEXT("WeavingCSharp", "Weaving C# code..."));
	if (!FCSProcHelper::InvokeUnrealSharpBuildTool(Weave))
	{
		return;
	}
	
	// Unload the user's assembly, to apply the new one.
	Progress.EnterProgressFrame(1, LOCTEXT("UnloadingAssembly", "Unloading Assembly..."));
	if (!FCSManager::Get().UnloadAssembly(FCSProcHelper::GetUserManagedProjectName()))
	{
		return;
	}

	// Load the user's assembly.
	Progress.EnterProgressFrame(1, LOCTEXT("LoadingAssembly", "Loading Assembly..."));
	if (!FCSManager::Get().LoadUserAssembly())
	{
		return;
	}

	// Reinstance all blueprints.
	Progress.EnterProgressFrame(1, LOCTEXT("ReinstancingBlueprints", "Reinstancing Blueprints..."));
	FCSReinstancer::Get().StartReinstancing();
}

void FUnrealSharpEditorModule::OnUnrealSharpInitialized()
{
	FCSManager& Manager = FCSManager::Get();
	
	// Deny any classes from being Edited in BP that's in the UnrealSharp package. Otherwise it would crash the engine.
	// Workaround for a hardcoded feature in the engine for Blueprints.
	FAssetToolsModule& AssetToolsModule = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools"));
	FName UnrealSharpPackageName = Manager.GetUnrealSharpPackage()->GetFName();
	AssetToolsModule.Get().GetWritableFolderPermissionList()->AddDenyListItem(UnrealSharpPackageName, UnrealSharpPackageName);
	
	UToolMenus::RegisterStartupCallback(FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FUnrealSharpEditorModule::RegisterMenus));

	FDirectoryWatcherModule& DirectoryWatcherModule = FModuleManager::LoadModuleChecked<FDirectoryWatcherModule>("DirectoryWatcher");
	IDirectoryWatcher* DirectoryWatcher = DirectoryWatcherModule.Get();
	FDelegateHandle Handle;

	FString FullScriptPath = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir() / "Script");

	if (!FPaths::DirectoryExists(FullScriptPath))
	{
		FPlatformFileManager::Get().GetPlatformFile().CreateDirectory(*FullScriptPath);
	}
	
	//Bind to directory watcher to look for changes in C# code.
	DirectoryWatcher->RegisterDirectoryChangedCallback_Handle(
		FullScriptPath,
		IDirectoryWatcher::FDirectoryChanged::CreateRaw(this, &FUnrealSharpEditorModule::OnCSharpCodeModified),
		Handle);

	FCSReinstancer::Get().Initialize();

	TickDelegate = FTickerDelegate::CreateRaw(this, &FUnrealSharpEditorModule::Tick);
	TickDelegateHandle = FTSTicker::GetCoreTicker().AddTicker(TickDelegate);
}

bool FUnrealSharpEditorModule::Tick(float DeltaTime)
{
	const UCSDeveloperSettings* Settings = GetDefault<UCSDeveloperSettings>();
	if (!Settings->bRequireFocusForHotReload || !bIsReloading || !FApp::HasFocus())
	{
		return true;
	}

	StartHotReload();
	bIsReloading = false;
	return true;
}

void FUnrealSharpEditorModule::RegisterMenus()
{
	FToolMenuOwnerScoped OwnerScoped(this);
	{
		UToolMenu* ToolbarMenu = UToolMenus::Get()->ExtendMenu("LevelEditor.LevelEditorToolBar.ModesToolBar");
		{
			FToolMenuSection& ToolbarSection = ToolbarMenu->FindOrAddSection("CompileCSharp");
			{
				ToolbarSection.AddEntry(FToolMenuEntry::InitToolBarButton(
				  "Compile c#",
				  FExecuteAction::CreateLambda([this]
				  {
				  		StartHotReload();
				  }),
				  INVTEXT("Compile C#"),
				  INVTEXT("Force recompile and reload of C# code"),
				  FSlateIcon(FAppStyle::GetAppStyleSetName(), "LevelEditor.Recompile")
			    ));
			}
		}
	}
}

#undef LOCTEXT_NAMESPACE
    
IMPLEMENT_MODULE(FUnrealSharpEditorModule, UnrealSharpEditor)
