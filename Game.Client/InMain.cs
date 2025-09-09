﻿using Game.Client.HUD;
using Game.Shared;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Mathematics;
using Source.Engine;
using Source.Engine.Client;
using Source.GUI.Controls;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Game.Client;

public struct KeyButtonState
{
	public InlineArray2<int> Down;
	public int State;
}

public enum MouseParams
{
	AccelThreshold1 = 0,
	AccelThreshold2,
	SpeedFactor,
	Num
}

public partial class Input(IServiceProvider provider, IClientMode ClientMode, ISurface Surface, IViewRender view, ThirdPersonManager ThirdPersonManager) : IInput
{
	ConVar cl_anglespeedkey = new("0.67", 0);
	ConVar cl_yawspeed = new("210", FCvar.None, "Client yaw speed.", -100000, 100000);
	ConVar cl_pitchspeed = new("225", FCvar.None, "Client pitch speed.", -100000, 100000);
	ConVar cl_pitchdown = new("89", FCvar.Cheat);
	ConVar cl_pitchup = new("89", FCvar.Cheat);
	ConVar cl_sidespeed = new("450", FCvar.Replicated | FCvar.Cheat);
	ConVar cl_upspeed = new("320", FCvar.Replicated | FCvar.Cheat);
	ConVar cl_forwardspeed = new("450", FCvar.Replicated | FCvar.Cheat);
	ConVar cl_backspeed = new("450", FCvar.Replicated | FCvar.Cheat);
	ConVar lookspring = new("0", FCvar.Archive);
	ConVar lookstrafe = new("0", FCvar.Archive);
	ConVar in_joystick = new("0", FCvar.Archive);
	ConVar thirdperson_platformer = new("0", 0, "Player will aim in the direction they are moving.");
	ConVar thirdperson_screenspace = new("0", 0, "Movement will be relative to the camera, eg: left means screen-left");
	ConVar sv_noclipduringpause = new("0", FCvar.Replicated | FCvar.Cheat, "If cheats are enabled, then you can noclip with the game paused (for doing screenshots, etc.).");

	KeyButtonState in_speed;
	KeyButtonState in_walk;
	KeyButtonState in_jlook;
	KeyButtonState in_strafe;
	KeyButtonState in_forward;
	KeyButtonState in_back;
	KeyButtonState in_moveleft;
	KeyButtonState in_moveright;
	KeyButtonState in_graph;
	KeyButtonState in_klook;
	KeyButtonState in_left;
	KeyButtonState in_right;
	KeyButtonState in_lookup;
	KeyButtonState in_lookdown;
	KeyButtonState in_use;
	KeyButtonState in_jump;
	KeyButtonState in_attack;
	KeyButtonState in_attack2;
	KeyButtonState in_up;
	KeyButtonState in_down;
	KeyButtonState in_duck;
	KeyButtonState in_reload;
	KeyButtonState in_alt1;
	KeyButtonState in_alt2;
	KeyButtonState in_score;
	KeyButtonState in_break;
	KeyButtonState in_zoom;
	KeyButtonState in_attack3;

	void KeyDown(ref KeyButtonState button, ReadOnlySpan<char> code) {
		int k = int.TryParse(code, out k) ? k : -1;

		if (k == button.Down[0] || k == button.Down[1])
			return;

		if (button.Down[0] == 0)
			button.Down[0] = k;
		else if (button.Down[1] == 0)
			button.Down[1] = k;
		else {
			if (code[0] != 0)
				DevMsg(1, "Three keys down for a button '%c' '%c' '%c'!\n", button.Down[0], button.Down[1], code[0]);
			return;
		}

		if ((button.State & 1) != 0)
			return;
		button.State |= 1 + 2;
	}

	void KeyUp(ref KeyButtonState b, ReadOnlySpan<char> c) {
		if (c == null || c.Length <= 0) {
			b.Down[0] = b.Down[1] = 0;
			b.State = 4;
			return;
		}

		int k = int.TryParse(c, out k) ? k : 0;

		if (b.Down[0] == k)
			b.Down[0] = 0;
		else if (b.Down[1] == k)
			b.Down[1] = 0;
		else
			return;

		if (b.Down[0] != 0 || b.Down[1] != 0)
			return;

		if ((b.State & 1) == 0)
			return;

		b.State &= ~1;
		b.State |= 4;
	}
	float KeyState(ref KeyButtonState key) {
		float val = 0f;
		int impulsedown, impulseup, down;

		impulsedown = key.State & 2;
		impulseup = key.State & 4;
		down = key.State & 1;

		if (impulsedown != 0 && impulseup == 0)
			val = down != 0 ? 0.5f : 0.0f;
		if (impulseup != 0 && impulsedown == 0)
			val = down != 0 ? 0.0f : 0.0f;
		if (impulsedown == 0 && impulseup == 0)
			val = down != 0 ? 1.0f : 0.0f;

		if (impulsedown != 0 && impulseup != 0)
			if (down != 0)
				val = 0.75f;
			else
				val = 0.25f;

		key.State &= 1;
		return val;
	}

	[ConCommand(name: "+moveup")] void IN_UpDown(in TokenizedCommand args) => KeyDown(ref in_up, args[1]);
	[ConCommand(name: "+movedown")] void IN_DownDown(in TokenizedCommand args) => KeyDown(ref in_down, args[1]);
	[ConCommand(name: "+left")] void IN_LeftDown(in TokenizedCommand args) => KeyDown(ref in_left, args[1]);
	[ConCommand(name: "+right")] void IN_RightDown(in TokenizedCommand args) => KeyDown(ref in_right, args[1]);
	[ConCommand(name: "+forward")] void IN_ForwardDown(in TokenizedCommand args) => KeyDown(ref in_forward, args[1]);
	[ConCommand(name: "+back")] void IN_BackDown(in TokenizedCommand args) => KeyDown(ref in_back, args[1]);
	[ConCommand(name: "+lookup")] void IN_LookupDown(in TokenizedCommand args) => KeyDown(ref in_lookup, args[1]);
	[ConCommand(name: "+lookdown")] void IN_LookdownDown(in TokenizedCommand args) => KeyDown(ref in_lookdown, args[1]);
	[ConCommand(name: "+strafe")] void IN_StrafeDown(in TokenizedCommand args) => KeyDown(ref in_strafe, args[1]);
	[ConCommand(name: "+moveleft")] void IN_MoveleftDown(in TokenizedCommand args) => KeyDown(ref in_moveleft, args[1]);
	[ConCommand(name: "+moveright")] void IN_MoverightDown(in TokenizedCommand args) => KeyDown(ref in_moveright, args[1]);
	[ConCommand(name: "+speed")] void IN_SpeedDown(in TokenizedCommand args) => KeyDown(ref in_speed, args[1]);
	[ConCommand(name: "+walk")] void IN_WalkDown(in TokenizedCommand args) => KeyDown(ref in_walk, args[1]);
	[ConCommand(name: "+attack")] void IN_AttackDown(in TokenizedCommand args) => KeyDown(ref in_attack, args[1]);
	[ConCommand(name: "+attack2")] void IN_Attack2Down(in TokenizedCommand args) => KeyDown(ref in_attack2, args[1]);
	[ConCommand(name: "+use")] void IN_UseDown(in TokenizedCommand args) => KeyDown(ref in_use, args[1]);
	[ConCommand(name: "+jump")] void IN_JumpDown(in TokenizedCommand args) => KeyDown(ref in_jump, args[1]);
	[ConCommand(name: "+klook")] void IN_KLookDown(in TokenizedCommand args) => KeyDown(ref in_klook, args[1]);
	[ConCommand(name: "+jlook")] void IN_JLookDown(in TokenizedCommand args) => KeyDown(ref in_jlook, args[1]);
	[ConCommand(name: "+duck")] void IN_DuckDown(in TokenizedCommand args) => KeyDown(ref in_duck, args[1]);
	[ConCommand(name: "+reload")] void IN_ReloadDown(in TokenizedCommand args) => KeyDown(ref in_reload, args[1]);
	[ConCommand(name: "+alt1")] void IN_Alt1Down(in TokenizedCommand args) => KeyDown(ref in_alt1, args[1]);
	[ConCommand(name: "+alt2")] void IN_Alt2Down(in TokenizedCommand args) => KeyDown(ref in_alt2, args[1]);
	[ConCommand(name: "+score")] void IN_ScoreDown(in TokenizedCommand args) => KeyDown(ref in_score, args[1]);
	[ConCommand(name: "+graph")] void IN_GraphDown(in TokenizedCommand args) => KeyDown(ref in_graph, args[1]);
	[ConCommand(name: "+break")] void IN_BreakDown(in TokenizedCommand args) => KeyDown(ref in_break, args[1]);
	[ConCommand(name: "+zoom")] void IN_ZoomDown(in TokenizedCommand args) => KeyDown(ref in_zoom, args[1]);
	[ConCommand(name: "+attack3")] void IN_Attack3Down(in TokenizedCommand args) => KeyDown(ref in_attack3, args[1]);



	[ConCommand(name: "-moveup")] void IN_UpUp(in TokenizedCommand args) => KeyUp(ref in_up, args[1]);
	[ConCommand(name: "-movedown")] void IN_DownUp(in TokenizedCommand args) => KeyUp(ref in_down, args[1]);
	[ConCommand(name: "-left")] void IN_LeftUp(in TokenizedCommand args) => KeyUp(ref in_left, args[1]);
	[ConCommand(name: "-right")] void IN_RightUp(in TokenizedCommand args) => KeyUp(ref in_right, args[1]);
	[ConCommand(name: "-forward")] void IN_ForwardUp(in TokenizedCommand args) => KeyUp(ref in_forward, args[1]);
	[ConCommand(name: "-back")] void IN_BackUp(in TokenizedCommand args) => KeyUp(ref in_back, args[1]);
	[ConCommand(name: "-lookup")] void IN_LookupUp(in TokenizedCommand args) => KeyUp(ref in_lookup, args[1]);
	[ConCommand(name: "-lookdown")] void IN_LookdownUp(in TokenizedCommand args) => KeyUp(ref in_lookdown, args[1]);
	[ConCommand(name: "-strafe")] void IN_StrafeUp(in TokenizedCommand args) => KeyUp(ref in_strafe, args[1]);
	[ConCommand(name: "-moveleft")] void IN_MoveleftUp(in TokenizedCommand args) => KeyUp(ref in_moveleft, args[1]);
	[ConCommand(name: "-moveright")] void IN_MoverightUp(in TokenizedCommand args) => KeyUp(ref in_moveright, args[1]);
	[ConCommand(name: "-speed")] void IN_SpeedUp(in TokenizedCommand args) => KeyUp(ref in_speed, args[1]);
	[ConCommand(name: "-walk")] void IN_WalkUp(in TokenizedCommand args) => KeyUp(ref in_walk, args[1]);
	[ConCommand(name: "-attack")] void IN_AttackUp(in TokenizedCommand args) => KeyUp(ref in_attack, args[1]);
	[ConCommand(name: "-attack2")] void IN_Attack2Up(in TokenizedCommand args) => KeyUp(ref in_attack2, args[1]);
	[ConCommand(name: "-use")] void IN_UseUp(in TokenizedCommand args) => KeyUp(ref in_use, args[1]);
	[ConCommand(name: "-jump")] void IN_JumpUp(in TokenizedCommand args) => KeyUp(ref in_jump, args[1]);
	[ConCommand(name: "-klook")] void IN_KLookUp(in TokenizedCommand args) => KeyUp(ref in_klook, args[1]);
	[ConCommand(name: "-jlook")] void IN_JLookUp(in TokenizedCommand args) => KeyUp(ref in_jlook, args[1]);
	[ConCommand(name: "-duck")] void IN_DuckUp(in TokenizedCommand args) => KeyUp(ref in_duck, args[1]);
	[ConCommand(name: "-reload")] void IN_ReloadUp(in TokenizedCommand args) => KeyUp(ref in_reload, args[1]);
	[ConCommand(name: "-alt1")] void IN_Alt1Up(in TokenizedCommand args) => KeyUp(ref in_alt1, args[1]);
	[ConCommand(name: "-alt2")] void IN_Alt2Up(in TokenizedCommand args) => KeyUp(ref in_alt2, args[1]);
	[ConCommand(name: "-score")] void IN_ScoreUp(in TokenizedCommand args) => KeyUp(ref in_score, args[1]);
	[ConCommand(name: "-graph")] void IN_GraphUp(in TokenizedCommand args) => KeyUp(ref in_graph, args[1]);
	[ConCommand(name: "-break")] void IN_BreakUp(in TokenizedCommand args) => KeyUp(ref in_break, args[1]);
	[ConCommand(name: "-zoom")] void IN_ZoomUp(in TokenizedCommand args) => KeyUp(ref in_zoom, args[1]);
	[ConCommand(name: "-attack3")] void IN_Attack3Up(in TokenizedCommand args) => KeyUp(ref in_attack3, args[1]);

	readonly Lazy<IBaseClientDLL> clientDLLLzy = new(provider.GetRequiredService<IBaseClientDLL>);
	IBaseClientDLL clientDLL => clientDLLLzy.Value;

	ClientState cl;
	IEngineClient engine;
	Hud Hud;

	public const int MULTIPLAYER_BACKUP = 90;
	UserCmd[] Commands = new UserCmd[MULTIPLAYER_BACKUP];

	public unsafe ref UserCmd GetUserCmd(int sequenceNumber) {
		ref UserCmd usercmd = ref Commands[MathLib.Modulo(sequenceNumber, MULTIPLAYER_BACKUP)];
		if (usercmd.CommandNumber != sequenceNumber)
			return ref UserCmd.NULL;

		return ref usercmd;
	}

	public void CreateMove(int sequenceNumber, double inputSampleFrametime, bool active) {
		int nextcmdnr = cl.LastOutgoingCommand + cl.ChokedCommands + 1;
		ref UserCmd cmd = ref Commands[MathLib.Modulo(nextcmdnr, MULTIPLAYER_BACKUP)];
		{
			cmd.Reset();
			cmd.CommandNumber = nextcmdnr;
			cmd.TickCount = cl.GetClientTickCount();

			if (active) {
				AdjustAngles(inputSampleFrametime);
				ComputeSideMove(ref cmd);
				ComputeUpwardMove(ref cmd);
				ComputeForwardMove(ref cmd);
				ScaleMovements(ref cmd);
				ControllerMove(inputSampleFrametime, ref cmd);
			}

			cmd.Buttons = GetButtonBits(0);
		}
	}

	public unsafe void ValidateUserCmd(UserCmd* f, int from) {

	}

	public unsafe bool WriteUsercmdDeltaToBuffer(bf_write buf, int from, int to, bool isNewCommand) {
		UserCmd nullcmd;
		UserCmd* f = null, t = null;

		int startbit = buf.BitsWritten;
		if (from == -1) {
			f = &nullcmd;
		}
		else {
			f = (UserCmd*)Unsafe.AsPointer(ref GetUserCmd(from));
			if (f == null) {
				f = &nullcmd;
			}
			else {
				ValidateUserCmd(f, from);
			}
		}

		t = (UserCmd*)Unsafe.AsPointer(ref GetUserCmd(to));
		if (t == null) {
			t = &nullcmd;
		}
		else {
			ValidateUserCmd(t, to);
		}

		UserCmd.WriteUsercmd(buf, in *t, in *f);
		if (buf.Overflowed) {
			Warning("WARNING! User command buffer overflow\n");
			return false;
		}

		return true;
	}

	public void EncodeUserCmdToBuffer(bf_write buf, int sequenceNumber) {
		ref UserCmd cmd = ref GetUserCmd(sequenceNumber);
		UserCmd.WriteUsercmd(buf, in cmd, in UserCmd.NULL);
	}

	public void DecodeUserCmdFromBuffer(bf_read buf, int sequenceNumber) {
		ref UserCmd cmd = ref Commands[MathLib.Modulo(sequenceNumber, MULTIPLAYER_BACKUP)];
		UserCmd.ReadUsercmd(buf, ref cmd, ref UserCmd.NULL);
	}

	public void Init() {
		cl = Singleton<ClientState>();
		engine = Singleton<IEngineClient>();
		Hud = Singleton<Hud>();

		MouseInitialized = false;
		MouseActive = false;

		Init_Mouse();
		Init_Keyboard();
		Init_Camera();
	}

	private void Init_Mouse() {
		PreviousMouseXPosition = 0.0f;
		PreviousMouseYPosition = 0.0f;

		MouseInitialized = true;
	}

	private void Init_Keyboard() {

	}

	private void Init_Camera() {
		CameraIsOrthographic = false;
	}

	bool CameraInterceptingMouse;
	bool MouseActive;
	bool MouseInitialized;
	bool CameraInThirdPerson;
	bool CameraIsOrthographic;

	float AccumulatedMouseXMovement;
	float AccumulatedMouseYMovement;
	float PreviousMouseXPosition;
	float PreviousMouseYPosition;
	double KeyboardSampleTime;

	public int KeyEvent(int down, ButtonCode code, ReadOnlySpan<char> currentBinding) {
		if ((code == ButtonCode.MouseLeft) || (code == ButtonCode.MouseRight)) {
			if (CameraInterceptingMouse)
				return 0;
		}

		ClientMode?.KeyInput(down, code, currentBinding);

		return 1;
	}

	public unsafe void ExtraMouseSample(double frametime, bool active) {
		UserCmd dummy = new();
		ref UserCmd cmd = ref dummy;
		cmd.Reset();

		QAngle viewangles;
		engine.GetViewAngles(out viewangles);
		QAngle originalViewangles = viewangles;

		if (active) {
			AdjustAngles(frametime);
			ComputeSideMove(ref cmd);
			ComputeUpwardMove(ref cmd);
			ComputeForwardMove(ref cmd);
			ScaleMovements(ref cmd);
			ControllerMove(frametime, ref cmd);
		}

		cmd.Buttons = GetButtonBits(0);
	}

	private void AdjustAngles(double frametime) {
		float speed = DetermineKeySpeed(frametime);
		if (speed <= 0.0f)
			return;

		engine.GetViewAngles(out QAngle viewangles);

		AdjustYaw(speed, viewangles);
		AdjustPitch(speed, viewangles);
		ClampAngles(viewangles);

		engine.SetViewAngles(viewangles);
	}

	private void ClampAngles(QAngle viewangles) {
		if (viewangles[PITCH] > cl_pitchdown.GetFloat())
			viewangles[PITCH] = cl_pitchdown.GetFloat();

		if (viewangles[PITCH] < -cl_pitchup.GetFloat())
			viewangles[PITCH] = -cl_pitchup.GetFloat();

		if (viewangles[ROLL] > 50)
			viewangles[ROLL] = 50;

		if (viewangles[ROLL] < -50)
			viewangles[ROLL] = -50;
	}

	private void AdjustPitch(float speed, QAngle viewangles) {
		if (!cl_mouselook.GetBool()) {
			if ((in_klook.State & 1) != 0) {
				view.StopPitchDrift();
				viewangles[PITCH] -= speed * cl_pitchspeed.GetFloat() * KeyState(ref in_forward);
				viewangles[PITCH] += speed * cl_pitchspeed.GetFloat() * KeyState(ref in_back);
			}

			float up = KeyState(ref in_lookup);
			float down = KeyState(ref in_lookdown);

			viewangles[PITCH] -= speed * cl_pitchspeed.GetFloat() * up;
			viewangles[PITCH] += speed * cl_pitchspeed.GetFloat() * down;

			if (up != 0 || down != 0) {
				view.StopPitchDrift();
			}
		}
	}

	private void AdjustYaw(float speed, QAngle viewangles) {
		if ((in_strafe.State & 1) == 0) {
			viewangles[YAW] -= speed * cl_yawspeed.GetFloat() * KeyState(ref in_right);
			viewangles[YAW] += speed * cl_yawspeed.GetFloat() * KeyState(ref in_left);
		}

		if (CAM_IsThirdPerson() && thirdperson_platformer.GetInt() != 0) {
			float side = KeyState(ref in_moveleft) - KeyState(ref in_moveright);
			float forward = KeyState(ref in_forward) - KeyState(ref in_back);

			if (side != 0 || forward != 0)
				viewangles[YAW] = MathLib.RAD2DEG(MathF.Atan2(side, forward));

			if (side != 0 || forward != 0 || KeyState(ref in_right) != 0 || KeyState(ref in_left) != 0)
				cam_idealyaw.SetValue(ThirdPersonManager.GetCameraOffsetAngles()[YAW] - viewangles[YAW]);
		}
	}

	private float DetermineKeySpeed(double frametime) {
		float speed = (float)frametime;

		if ((in_speed.State & 1) != 0)
			speed *= cl_anglespeedkey.GetFloat();

		return speed;
	}

	private void ControllerMove(double frametime, ref UserCmd cmd) {
		if (!CameraInterceptingMouse && MouseActive)
			MouseMove(ref cmd);
	}

	private void MouseMove(ref UserCmd cmd) {
		engine.GetViewAngles(out QAngle viewangles);
		view.StopPitchDrift();

		if (!CameraInterceptingMouse && !Surface.IsCursorVisible()) {
			AccumulateMouse();
			GetAccumulatedMouseDeltasAndResetAccumulators(out float mx, out float my);
			GetMouseDelta(mx, my, out float mouse_x, out float mouse_y);
			ScaleMouse(ref mouse_x, ref mouse_y);
			ClientMode.OverrideMouseInput(ref mouse_x, ref mouse_y);
			ApplyMouse(viewangles, cmd, mouse_x, mouse_y);
			ResetMouse();
		}

		engine.SetViewAngles(in viewangles);
	}

	private void ApplyMouse(QAngle viewangles, UserCmd cmd, float mouse_x, float mouse_y) {

	}

	private void ScaleMouse(ref float x, ref float y) {
		float mx = x;
		float my = y;

		float mouse_sensitivity = Hud.GetSensitivity() != 0 ? Hud.GetSensitivity() : sensitivity.GetFloat();

		if (m_customaccel.GetInt() == 1 || m_customaccel.GetInt() == 2) {
			float raw_mouse_movement_distance = MathF.Sqrt(mx * mx + my * my);
			float acceleration_scale = m_customaccel_scale.GetFloat();
			float accelerated_sensitivity_max = m_customaccel_max.GetFloat();
			float accelerated_sensitivity_exponent = m_customaccel_exponent.GetFloat();
			float accelerated_sensitivity = MathF.Pow(raw_mouse_movement_distance, accelerated_sensitivity_exponent) * acceleration_scale + mouse_sensitivity;

			if (accelerated_sensitivity_max > 0.0001f &&
				accelerated_sensitivity > accelerated_sensitivity_max) {
				accelerated_sensitivity = accelerated_sensitivity_max;
			}

			x *= accelerated_sensitivity;
			y *= accelerated_sensitivity;

			if (m_customaccel.GetInt() == 2 || m_customaccel.GetInt() == 4) {
				x *= m_yaw.GetFloat();
				y *= m_pitch.GetFloat();
			}
		}
		else if (m_customaccel.GetInt() == 3) {
			float raw_mouse_movement_distance_squared = mx * mx + my * my;
			float fExp = Math.Max(0.0f, (m_customaccel_exponent.GetFloat() - 1.0f) / 2.0f);
			float accelerated_sensitivity = MathF.Pow(raw_mouse_movement_distance_squared, fExp) * mouse_sensitivity;

			x *= accelerated_sensitivity;
			y *= accelerated_sensitivity;
		}
		else {
			x *= mouse_sensitivity;
			y *= mouse_sensitivity;
		}
	}

	ConVar cl_mouseenable = new("1", 0);

	private void GetMouseDelta(float inmousex, float inmousey, out float outmousex, out float outmousey) {
		if (m_filter.GetBool()) {
			outmousex = (inmousex + PreviousMouseXPosition) * 0.5f;
			outmousey = (inmousey + PreviousMouseYPosition) * 0.5f;
		}
		else {
			outmousex = inmousex;
			outmousey = inmousey;
		}

		PreviousMouseXPosition = inmousex;
		PreviousMouseYPosition = inmousey;
	}

	private void GetAccumulatedMouseDeltasAndResetAccumulators(out float mx, out float my) {
		mx = AccumulatedMouseXMovement;
		my = AccumulatedMouseYMovement;

		// todo: raw input?

		AccumulatedMouseXMovement = 0;
		AccumulatedMouseYMovement = 0;
	}

	private void AccumulateMouse() {
		if (!cl_mouseenable.GetBool())
			return;

		if (!cl_mouselook.GetBool()) 
			return;
		
		engine.GetScreenSize(out int w, out int h);

		if (!CameraInterceptingMouse && Surface.IsCursorLocked()) {
			engine.GetMouseDelta(out int dx, out int dy);
			AccumulatedMouseXMovement += dx;
			AccumulatedMouseYMovement += dy;

			ResetMouse();
		}
		else if (MouseActive) {
			GetMousePos(out int ox, out int oy);

			int new_ox = Math.Clamp(ox, 0, w - 1);
			int new_oy = Math.Clamp(oy, 0, h - 1);

			if (ox != new_ox || oy != new_oy) 
				SetMousePos(new_ox, new_oy);
		}
	}


	private void ScaleMovements(ref UserCmd cmd) {
		return; // nothing?
	}

	public bool CAM_IsThirdPerson() => CameraInThirdPerson;

	private void ComputeForwardMove(ref UserCmd cmd) {
		if (CAM_IsThirdPerson() && thirdperson_platformer.GetInt() != 0) {
			float movement = (KeyState(ref in_forward) == 0 ? false : true
				|| KeyState(ref in_moveright) == 0 ? false : true
				|| KeyState(ref in_back) == 0 ? false : true
				|| KeyState(ref in_moveleft) == 0 ? false : true
			) ? 1 : 0;

			cmd.ForwardMove += cl_forwardspeed.GetFloat() * movement;
			return;
		}

		if (CAM_IsThirdPerson() && thirdperson_screenspace.GetInt() != 0) {
			float ideal_yaw = cam_idealyaw.GetFloat();
			float ideal_sin = MathF.Sin(MathLib.DEG2RAD(ideal_yaw));
			float ideal_cos = MathF.Cos(MathLib.DEG2RAD(ideal_yaw));

			float movement = ideal_cos * KeyState(ref in_forward)
				+ ideal_sin * KeyState(ref in_moveright)
				+ -ideal_cos * KeyState(ref in_back)
				+ -ideal_sin * KeyState(ref in_moveleft);

			cmd.ForwardMove += cl_forwardspeed.GetFloat() * movement;

			return;
		}
		if ((in_klook.State & 1) == 0) {
			cmd.ForwardMove += cl_forwardspeed.GetFloat() * KeyState(ref in_forward);
			cmd.ForwardMove -= cl_backspeed.GetFloat() * KeyState(ref in_back);
		}
	}

	private void ComputeSideMove(ref UserCmd cmd) {
		if (CAM_IsThirdPerson() && thirdperson_platformer.GetInt() != 0) {
			return;
		}

		if (CAM_IsThirdPerson() && thirdperson_screenspace.GetInt() != 0) {
			float ideal_yaw = cam_idealyaw.GetFloat();
			float ideal_sin = MathF.Sin(MathLib.DEG2RAD(ideal_yaw));
			float ideal_cos = MathF.Cos(MathLib.DEG2RAD(ideal_yaw));

			float movement = ideal_cos * KeyState(ref in_moveright)
				+ ideal_sin * KeyState(ref in_back)
				+ -ideal_cos * KeyState(ref in_moveleft)
				+ -ideal_sin * KeyState(ref in_forward);

			cmd.SideMove += cl_sidespeed.GetFloat() * movement;

			return;
		}

		if ((in_strafe.State & 1) != 0) {
			cmd.SideMove += cl_sidespeed.GetFloat() * KeyState(ref in_right);
			cmd.SideMove -= cl_sidespeed.GetFloat() * KeyState(ref in_left);
		}

		cmd.SideMove += cl_sidespeed.GetFloat() * KeyState(ref in_moveright);
		cmd.SideMove -= cl_sidespeed.GetFloat() * KeyState(ref in_moveleft);
	}

	private void ComputeUpwardMove(ref UserCmd cmd) {
		cmd.UpMove += cl_upspeed.GetFloat() * KeyState(ref in_up);
		cmd.UpMove -= cl_upspeed.GetFloat() * KeyState(ref in_down);
	}

	private InButtons GetButtonBits(int resetState) {
		InButtons bits = 0;

		CalcButtonBits(ref bits, InButtons.Speed, ClearInputState, ref in_speed, resetState);
		CalcButtonBits(ref bits, InButtons.Walk, ClearInputState, ref in_walk, resetState);
		CalcButtonBits(ref bits, InButtons.Attack, ClearInputState, ref in_attack, resetState);
		CalcButtonBits(ref bits, InButtons.Duck, ClearInputState, ref in_duck, resetState);
		CalcButtonBits(ref bits, InButtons.Jump, ClearInputState, ref in_jump, resetState);
		CalcButtonBits(ref bits, InButtons.Forward, ClearInputState, ref in_forward, resetState);
		CalcButtonBits(ref bits, InButtons.Back, ClearInputState, ref in_back, resetState);
		CalcButtonBits(ref bits, InButtons.Use, ClearInputState, ref in_use, resetState);
		CalcButtonBits(ref bits, InButtons.Left, ClearInputState, ref in_left, resetState);
		CalcButtonBits(ref bits, InButtons.Right, ClearInputState, ref in_right, resetState);
		CalcButtonBits(ref bits, InButtons.MoveLeft, ClearInputState, ref in_moveleft, resetState);
		CalcButtonBits(ref bits, InButtons.MoveRight, ClearInputState, ref in_moveright, resetState);
		CalcButtonBits(ref bits, InButtons.Attack2, ClearInputState, ref in_attack2, resetState);
		CalcButtonBits(ref bits, InButtons.Reload, ClearInputState, ref in_reload, resetState);
		CalcButtonBits(ref bits, InButtons.Alt1, ClearInputState, ref in_alt1, resetState);
		CalcButtonBits(ref bits, InButtons.Alt2, ClearInputState, ref in_alt2, resetState);
		CalcButtonBits(ref bits, InButtons.Score, ClearInputState, ref in_score, resetState);
		CalcButtonBits(ref bits, InButtons.Zoom, ClearInputState, ref in_zoom, resetState);
		CalcButtonBits(ref bits, InButtons.Attack3, ClearInputState, ref in_attack3, resetState);

		// TODO: duck toggle
		// TODO: cancel

		if ((Hud.KeyBits & InButtons.Weapon1) != 0)
			bits |= InButtons.Weapon1;

		if ((Hud.KeyBits & InButtons.Weapon2) != 0)
			bits |= InButtons.Weapon2;

		bits &= ClearInputState;

		if (resetState != 0)
			ClearInputState = 0;

		return bits;
	}

	private void CalcButtonBits(ref InButtons bits, InButtons in_button, InButtons in_ignore, ref KeyButtonState button, int reset) {
		if ((button.State & 3) != 0)
			bits |= in_button;

		int clearmask = ~2;
		if ((in_ignore & in_button) != 0)
			clearmask = ~3;

		if (reset != 0)
			button.State &= clearmask;
	}

	InButtons ClearInputState;
}
