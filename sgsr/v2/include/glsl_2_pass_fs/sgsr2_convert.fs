#version 320 es

//============================================================================================================
//
//
//                  Copyright (c) 2024, Qualcomm Innovation Center, Inc. All rights reserved.
//                              SPDX-License-Identifier: BSD-3-Clause
//
//============================================================================================================

precision highp float;
precision highp int;

layout(location = 0) out vec4 MotionDepthClipAlphaBuffer;
layout(location = 0) in highp vec2 texCoord;

layout(set = 0, binding = 1) uniform mediump sampler2D InputDepth;
layout(set = 0, binding = 2) uniform mediump sampler2D InputVelocity;

layout(std140, set = 0, binding = 0) uniform Params
{
    vec4                 clipToPrevClip[4];
    vec2                 renderSize;
    vec2                 outputSize;
    vec2                 renderSizeRcp;
    vec2                 outputSizeRcp;
    vec2                 jitterOffset;
    vec2                 scaleRatio;
    float                cameraFovAngleHor;
    float                minLerpContribution;
    float                reset;
    uint                 bSameCamera;
} params;

vec2 decodeVelocityFromTexture(vec2 ev) {
    const float inv_div = 1.0f / (0.499f * 0.5f);
    vec2 dv;
    dv.xy = ev.xy * inv_div - 32767.0f / 65535.0f * inv_div;
    //dv.z = uintBitsToFloat((uint(round(ev.z * 65535.0f)) << 16) | uint(round(ev.w * 65535.0f)));
    return dv;
}

void main()
{
    uvec2 InputPos = uvec2(texCoord * params.renderSize);
    vec2 gatherCoord = texCoord - vec2(0.5) * params.renderSizeRcp;

    
    // texture gather to find nearest depth
    //      a  b  c  d
    //      e  f  g  h
    //      i  j  k  l
    //      m  n  o  p
    //btmLeft mnji
    //btmRight oplk
    //topLeft  efba
    //topRight ghdc

    vec4 btmLeft = textureGather(InputDepth, gatherCoord, 0);
    vec2 v10 = vec2(params.renderSizeRcp.x * 2.0f, 0.0);
    vec4 btmRight = textureGather(InputDepth,(gatherCoord+v10), 0);
    vec2 v12 = vec2(0.0, params.renderSizeRcp.y * 2.0f);
	vec4 topLeft = textureGather(InputDepth,(gatherCoord+v12), 0);
	vec2 v14 = vec2(params.renderSizeRcp.x * 2.0f, params.renderSizeRcp.y * 2.0f);
	vec4 topRight = textureGather(InputDepth,(gatherCoord+v14), 0);
	float maxC = min(min(min(btmLeft.z,btmRight.w),topLeft.y),topRight.x);
	float btmLeft4 = min(min(min(btmLeft.y,btmLeft.x),btmLeft.z),btmLeft.w);
	float btmLeftMax9 = min(topLeft.x,min(min(maxC,btmLeft4),btmRight.x));

    float depthclip = 0.0;
    if (maxC < 1.0 - 1.0e-05f)
    {
        float btmRight4 = min(min(min(btmRight.y,btmRight.x),btmRight.z),btmRight.w);
        float topLeft4 = min(min(min(topLeft.y,topLeft.x),topLeft.z),topLeft.w);
        float topRight4 = min(min(min(topRight.y,topRight.x),topRight.z),topRight.w);

        float Wdepth = 0.0;
        float Ksep = 1.37e-05f;
        float Kfov = params.cameraFovAngleHor;
        float diagonal_length = length(params.renderSize);
        float Ksep_Kfov_diagonal = Ksep * Kfov * diagonal_length;

		float Depthsep = Ksep_Kfov_diagonal * (1.0 - maxC);
		float EPSILON = 1.19e-07f;
		Wdepth += clamp((Depthsep / (abs(maxC - btmLeft4) + EPSILON)), 0.0, 1.0);
		Wdepth += clamp((Depthsep / (abs(maxC - btmRight4) + EPSILON)), 0.0, 1.0);
		Wdepth += clamp((Depthsep / (abs(maxC - topLeft4) + EPSILON)), 0.0, 1.0);
		Wdepth += clamp((Depthsep / (abs(maxC - topRight4) + EPSILON)), 0.0, 1.0);
        depthclip = clamp(1.0f - Wdepth * 0.25, 0.0, 1.0);
    }

    //refer to ue/fsr2 PostProcessFFX_FSR2ConvertVelocity.usf, and using nearest depth for dilated motion

    vec4 EncodedVelocity = texelFetch(InputVelocity, ivec2(InputPos), 0);

    vec2 motion;
    if (EncodedVelocity.x > 0.0)
    {
        motion = decodeVelocityFromTexture(EncodedVelocity.xy);
    }
    else
    {
#ifdef REQUEST_NDC_Y_UP
        vec2 ScreenPos = vec2(2.0f * texCoord.x - 1.0f, 1.0f - 2.0f * texCoord.y);
#else
        vec2 ScreenPos = vec2(2.0f * texCoord - 1.0f);
#endif
        vec3 Position = vec3(ScreenPos, btmLeftMax9);    //this_clip
        vec4 PreClip = params.clipToPrevClip[3] + ((params.clipToPrevClip[2] * Position.z) + ((params.clipToPrevClip[1] * ScreenPos.y) + (params.clipToPrevClip[0] * ScreenPos.x)));
        vec2 PreScreen = PreClip.xy / PreClip.w;
        motion = Position.xy - PreScreen;
    }
    MotionDepthClipAlphaBuffer = vec4(motion, depthclip, 0.0);

}