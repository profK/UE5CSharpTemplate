// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "Subsystems/LocalPlayerSubsystem.h"
#include "CSLocalPlayerSubsystem.generated.h"

UCLASS(Blueprintable, BlueprintType, Abstract)
class UCSLocalPlayerSubsystem : public ULocalPlayerSubsystem
{
	GENERATED_BODY()

	// USubsystem Begin
	
	virtual void Initialize(FSubsystemCollectionBase& Collection) override
	{
		Super::Initialize(Collection);
		K2_Initialize();
	}
  
	virtual void Deinitialize() override
	{
		Super::Deinitialize();
		K2_Deinitialize();
	}
  
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override
	{
		if (!Super::ShouldCreateSubsystem(Outer))
		{
			return false;
		}
  
		return K2_ShouldCreateSubsystem();
	}

	// End

	// ULocalPlayerSubsystem
	virtual void PlayerControllerChanged(APlayerController* NewPlayerController) override
	{
		Super::PlayerControllerChanged(NewPlayerController);
		K2_PlayerControllerChanged(NewPlayerController);
	}
	// End
	
protected:

	UFUNCTION(BlueprintImplementableEvent, meta = (ScriptName = "PlayerControllerChanged"), Category = "Managed Subsystems")
	bool K2_PlayerControllerChanged(APlayerController* NewPlayerController) const;

	UFUNCTION(BlueprintNativeEvent, meta = (ScriptName = "ShouldCreateSubsystem"), Category = "Managed Subsystems")
	bool K2_ShouldCreateSubsystem() const;
  
	UFUNCTION(BlueprintImplementableEvent, meta = (ScriptName = "Initialize"), Category = "Managed Subsystems")
	void K2_Initialize();
  
	UFUNCTION(BlueprintImplementableEvent, meta = (ScriptName = "Deinitialize"), Category = "Managed Subsystems")
	void K2_Deinitialize();
	
};
