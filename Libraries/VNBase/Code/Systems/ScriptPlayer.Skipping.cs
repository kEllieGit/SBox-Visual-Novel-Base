﻿using System;
using VNBase.UI;

namespace VNBase;

public sealed partial class ScriptPlayer
{
	public void Skip()
	{
		if ( ActiveScript is null || ActiveLabel is null )
		{
			return;
		}

		if ( !CanSkip() )
		{
			return;
		}

		var currentLabel = ActiveLabel;
		while ( currentLabel?.AfterLabel is not null )
		{
			ExecuteAfterLabel();
			SkipDialogueEffect();
			currentLabel = ActiveLabel;
			
			// Check if we hit input.
			if ( currentLabel.ActiveInput is not null )
			{
				return;
			}

			// Check if we hit a choice.
			if ( currentLabel.Choices.Count > 0 )
			{
				return;
			}
		}
	}

	/// <summary>
	/// Checks if the current dialogue section can be skipped.
	/// </summary>
	public bool CanSkip()
	{
		if ( ActiveScript is null || ActiveLabel is null )
		{
			return false;
		}

		// TODO: Automatic mode skipping is broken. Investigate.
		if ( AutomaticMode )
		{
			return false;
		}

		var hasTextInput = Hud?.GetSubPanel<TextInput>() is not null;
		if ( hasTextInput )
		{
			return false;
		}
		
		var hasChoices = ActiveLabel.Choices.Count > 0;
		return !hasChoices;
	}
}
