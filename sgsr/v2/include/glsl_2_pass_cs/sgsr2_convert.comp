#version 320 es

//============================================================================================================
//
//
//                  Copyright (c) 2024, Qualcomm Innovation Center, Inc. All rights reserved.
//                              SPDX-License-Identifier: BSD-3-Clause
//
//============================================================================================================

vec2 decodeVelocityFromTexture(vec2 ev) {
    const float inv_div = 1.0f / (0.499f * 0.5f);
    vec2 dv;
    dv.xy = ev.xy * inv_div - 32767.0f / 65535.0f * inv_div;
    //dv.z = uintBitsToFloat((uint(round(ev.z * 65535.0f)) << 16) | uint(round(ev.w * 65535.0f)));
    return dv;
}

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 1) uniform highp sampler2D InputColor;
layout(binding = 2) uniform highp sampler2D InputDepth;
layout(binding = 3) uniform highp sampler2D InputVelocity;
layout(binding = 0, rgba16f) uniform writeonly mediump image2D MotionDepthClipAlphaBuffer;
layout(binding = 1, r32ui) uniform writeonly highp uimage2D YCoCgColor;

layout(binding = 0) uniform readonly Params
{
    uvec2                renderSize;
    uvec2                displaySize;
    vec2                 ViewportSizeInverse;
    vec2                 displaySizeRcp;
    vec2                 jitterOffset;
    vec4                 clipToPrevClip[4];
    float                preExposure;
    float                cameraFovAngleHor;
    float                cameraNear;
    float                MinLerpContribution;
    uint                 bSameCamera;
    uint                 reset;
} params;


void main()
{
    mediump float Exposure_co_rcp = params.preExposure;
    vec2 ViewportSizeInverse = params.ViewportSizeInverse.xy;
    uvec2 InputPos = gl_GlobalInvocationID.xy;

    vec2 gatherCoord = vec2(gl_GlobalInvocationID.xy) * ViewportSizeInverse;
    vec2 ViewportUV = gatherCoord + vec2(0.5) * ViewportSizeInverse;

    //derived from ffx_fsr2_reconstruct_dilated_velocity_and_previous_depth.h
    //FindNearestDepth

    vec4 topleft = textureGather(InputDepth, gatherCoord, 0);
    vec2 v10 = vec2(ViewportSizeInverse.x*2.0, 0.0);
    vec4 topRight = textureGather(InputDepth,(gatherCoord+v10), 0);
    vec2 v12 = vec2(0.0, ViewportSizeInverse.y*2.0);
	vec4 bottomLeft = textureGather(InputDepth,(gatherCoord+v12), 0);
	vec2 v14 = vec2(ViewportSizeInverse.x*2.0, ViewportSizeInverse.y*2.0);
	vec4 bottomRight = textureGather(InputDepth,(gatherCoord+v14), 0);
	float maxC = min(min(min(topleft.y,topRight.x),bottomLeft.z),bottomRight.w);
	float topleft4 = min(min(min(topleft.y,topleft.x),topleft.z),topleft.w);
	float topLeftMax9 = min(bottomLeft.w,min(min(maxC,topleft4),topRight.w));

    float depthclip = 0.0;
    if (maxC < 1.0 - 1.0e-05f)
    {
        float topRight4 = min(min(min(topRight.y,topRight.x),topRight.z),topRight.w);
        float bottomLeft4 = min(min(min(bottomLeft.y,bottomLeft.x),bottomLeft.z),bottomLeft.w);
        float bottomRight4 = min(min(min(bottomRight.y,bottomRight.x),bottomRight.z),bottomRight.w);

        float Wdepth = 0.0;
        float Ksep = 1.37e-05f;
        float Kfov = params.cameraFovAngleHor;
        float diagonal_length = length(vec2(params.renderSize));
        float Ksep_Kfov_diagonal = Ksep * Kfov * diagonal_length;

		float Depthsep = Ksep_Kfov_diagonal * (1.0 - maxC);
		float EPSILON = 1.19e-07f;
		Wdepth += clamp((Depthsep / (abs(maxC - topleft4) + EPSILON)), 0.0, 1.0);
		Wdepth += clamp((Depthsep / (abs(maxC - topRight4) + EPSILON)), 0.0, 1.0);
		Wdepth += clamp((Depthsep / (abs(maxC - bottomLeft4) + EPSILON)), 0.0, 1.0);
		Wdepth += clamp((Depthsep / (abs(maxC - bottomRight4) + EPSILON)), 0.0, 1.0);
        depthclip = clamp(1.0f - Wdepth*0.25, 0.0, 1.0);
    }

    //refer to ue/fsr2 PostProcessFFX_FSR2ConvertVelocity.usf, and using nearest depth for dilated motion

    vec4 EncodedVelocity = texelFetch(InputVelocity, ivec2(gl_GlobalInvocationID.xy), 0);

    vec2 motion;
    if (EncodedVelocity.x > 0.0)
    {
        motion = decodeVelocityFromTexture(EncodedVelocity.xy);
    }
    else
    {
#ifdef REQUEST_NDC_Y_UP
        vec2 ScreenPos = vec2(2.0f * ViewportUV.x - 1.0f, 1.0f - 2.0f * ViewportUV.y);
#else
         vec2 ScreenPos = vec2(2.0f * ViewportUV - 1.0f);
#endif
        vec3 Position = vec3(ScreenPos, topLeftMax9);    //this_clip
        vec4 PreClip = params.clipToPrevClip[3] + ((params.clipToPrevClip[2] * Position.z) + ((params.clipToPrevClip[1] * ScreenPos.y) + (params.clipToPrevClip[0] * ScreenPos.x)));
        vec2 PreScreen = PreClip.xy / PreClip.w;
        motion = Position.xy - PreScreen;
    }

    ////////////compute luma
    mediump vec3 Colorrgb = texelFetch(InputColor, ivec2(InputPos), 0).xyz;

    ///simple tonemap
    float ColorMax = max(max(Colorrgb.x, Colorrgb.y), Colorrgb.z) + Exposure_co_rcp;
    Colorrgb /= vec3(ColorMax);

    vec3 Colorycocg;
    Colorycocg.x = 0.25 * (Colorrgb.x + 2.0 * Colorrgb.y + Colorrgb.z);
    Colorycocg.y = clamp(0.5 * Colorrgb.x + 0.5 - 0.5 * Colorrgb.z, 0.0, 1.0);
    Colorycocg.z = clamp(Colorycocg.x + Colorycocg.y - Colorrgb.x, 0.0, 1.0);

    //now color YCoCG all in the range of [0,1]
    uint x11 = uint(Colorycocg.x * 2047.5);
    uint y11 = uint(Colorycocg.y * 2047.5);
    uint z10 = uint(Colorycocg.z * 1023.5);

    imageStore(YCoCgColor, ivec2(gl_GlobalInvocationID.xy), uvec4(((x11 << 21u) | (y11 << 10u)) | z10));

    mediump vec4 v29 = vec4(motion, depthclip, ColorMax);
    imageStore(MotionDepthClipAlphaBuffer, ivec2(gl_GlobalInvocationID.xy), v29);
}