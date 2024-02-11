#version 300 es

//============================================================================================================
//
//
//                  Copyright (c) 2023, Qualcomm Innovation Center, Inc. All rights reserved.
//                              SPDX-License-Identifier: BSD-3-Clause
//
//============================================================================================================

precision mediump float;
precision highp int;

////////////////////////
// USER CONFIGURATION //
////////////////////////

/*
* Operation modes:
* RGBA -> 1
* RGBY -> 3
* LERP -> 4
*/
#define OperationMode 1

#define EdgeThreshold 8.0/255.0

#define EdgeSharpness 2.0

// #define UseUniformBlock

////////////////////////
////////////////////////
////////////////////////

#if defined(UseUniformBlock)
layout (set=0, binding = 0) uniform UniformBlock
{
	highp vec4 ViewportInfo[1];
};
layout(set = 0, binding = 1) uniform mediump sampler2D ps0;
#else

// ViewportInfo should be a vec4 containing {1.0/low_res_tex_width, 1.0/low_res_tex_height, low_res_tex_width, low_res_tex_height}.
// The `xy` components will be used to shift UVs to read adjacent texels.
// The `zw` components will be used to map from UV space [0,1][0,1] to image space [0, w][0, h].
uniform highp vec4 ViewportInfo[1];

// ps0 is the sampler for the low resolution texture to upscale.
uniform mediump sampler2D ps0;

#endif  // if defined(UseUniformBlock)

// in_TEXCOORD0 is the texture coord for the current fragment; passed by the vertex shader.
layout(location=0) in highp vec4 in_TEXCOORD0;

// out_Target0 is the final fragment color.
layout(location=0) out vec4 out_Target0;

float fastLanczos2(float x)
{
	float wA = x-4.0;
	float wB = x*wA-wA;
	wA *= wA;
	return wB*wA;
}

vec2 weightY(float dx, float dy,float c, float std)
{
	float x = ((dx*dx)+(dy* dy))* 0.55 + clamp(abs(c)*std, 0.0, 1.0);
	float w = fastLanczos2(x);
	return vec2(w, w * c);	
}

void main()
{
	const int mode = OperationMode;
	float edgeThreshold = EdgeThreshold;
	float edgeSharpness = EdgeSharpness;

	// Sample the low res texture using current texture coordinates (in UV space).
	vec4 color;
	if(mode == 1)
		color.xyz = textureLod(ps0,in_TEXCOORD0.xy,0.0).xyz;
	else
		color.xyzw = textureLod(ps0,in_TEXCOORD0.xy,0.0).xyzw;

	highp float xCenter;
	xCenter = abs(in_TEXCOORD0.x+-0.5);
	highp float yCenter;
	yCenter = abs(in_TEXCOORD0.y+-0.5);

	//todo: config the SR region based on needs
	//if ( mode!=4 && xCenter*xCenter+yCenter*yCenter<=0.4 * 0.4)
	if ( mode!=4)
	{
		// Compute the coordinate for the center of the texel in image space.
		highp vec2 imgCoord = ((in_TEXCOORD0.xy*ViewportInfo[0].zw)+vec2(-0.5,0.5));
		highp vec2 imgCoordPixel = floor(imgCoord);
		// Remap the coordinate for the center of the texel in image space to UV space.
		highp vec2 coord = (imgCoordPixel*ViewportInfo[0].xy);
		vec2 pl = (imgCoord+(-imgCoordPixel));
		// Gather the `[mode]` components (ex: `.y` if mode is 1) of the 4 texels located around `coord`.
		vec4  left = textureGather(ps0,coord, mode);

		float edgeVote = abs(left.z - left.y) + abs(color[mode] - left.y)  + abs(color[mode] - left.z) ;
		if(edgeVote > edgeThreshold)
		{
			// Shift coord to the right by 1 texel. `coord` will be pointing to the same texel originally sampled
			// l.84 or 86 (The texel at UV in_TEXCOORD0 in the low res texture).
			coord.x += ViewportInfo[0].x;

			// Gather components for the texels located to the right of coord (the original sampled texel).
			vec4 right = textureGather(ps0,coord + highp vec2(ViewportInfo[0].x, 0.0), mode);
			// Gather components for the texels located to up and down of coord (the original sampled texel).
			vec4 upDown;
			upDown.xy = textureGather(ps0,coord + highp vec2(0.0, -ViewportInfo[0].y),mode).wz;
			upDown.zw  = textureGather(ps0,coord+ highp vec2(0.0, ViewportInfo[0].y), mode).yx;

			float mean = (left.y+left.z+right.x+right.w)*0.25;
			left = left - vec4(mean);
			right = right - vec4(mean);
			upDown = upDown - vec4(mean);
			color.w =color[mode] - mean;

			float sum = (((((abs(left.x)+abs(left.y))+abs(left.z))+abs(left.w))+(((abs(right.x)+abs(right.y))+abs(right.z))+abs(right.w)))+(((abs(upDown.x)+abs(upDown.y))+abs(upDown.z))+abs(upDown.w)));				
			float std = 2.181818/sum;
			
			vec2 aWY = weightY(pl.x, pl.y+1.0, upDown.x,std);				
			aWY += weightY(pl.x-1.0, pl.y+1.0, upDown.y,std);
			aWY += weightY(pl.x-1.0, pl.y-2.0, upDown.z,std);
			aWY += weightY(pl.x, pl.y-2.0, upDown.w,std);			
			aWY += weightY(pl.x+1.0, pl.y-1.0, left.x,std);
			aWY += weightY(pl.x, pl.y-1.0, left.y,std);
			aWY += weightY(pl.x, pl.y, left.z,std);
			aWY += weightY(pl.x+1.0, pl.y, left.w,std);
			aWY += weightY(pl.x-1.0, pl.y-1.0, right.x,std);
			aWY += weightY(pl.x-2.0, pl.y-1.0, right.y,std);
			aWY += weightY(pl.x-2.0, pl.y, right.z,std);
			aWY += weightY(pl.x-1.0, pl.y, right.w,std);

			float finalY = aWY.y/aWY.x;

			float maxY = max(max(left.y,left.z),max(right.x,right.w));
			float minY = min(min(left.y,left.z),min(right.x,right.w));
			finalY = clamp(edgeSharpness*finalY, minY, maxY);
					
			float deltaY = finalY -color.w;	
			
			//smooth high contrast input
			deltaY = clamp(deltaY, -23.0 / 255.0, 23.0 / 255.0);

			color.x = clamp((color.x+deltaY),0.0,1.0);
			color.y = clamp((color.y+deltaY),0.0,1.0);
			color.z = clamp((color.z+deltaY),0.0,1.0);
		}
	}

	color.w = 1.0;  //assume alpha channel is not used
	out_Target0.xyzw = color;
}
