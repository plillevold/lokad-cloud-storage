﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using Lokad.Cloud.Samples.MapReduce;

namespace Lokad.Cloud.Samples.MapReduce
{
	/// <summary>Implements helper methods.</summary>
	public static class Helpers
	{
		/// <summary>Slices an input bitmap into several parts.</summary>
		/// <param name="input">The input bitmap.</param>
		/// <param name="sliceCount">The number of slices.</param>
		/// <returns>The output bitmap.</returns>
		public static Bitmap[] SliceBitmap(Bitmap input, int sliceCount)
		{
			// Simply split the bitmap in vertical slices
			
			var outputBitmaps = new Bitmap[sliceCount];
			int sliceWidth = input.Width / sliceCount;

			int processedWidth = 0;
			for(int i = 0; i < sliceCount; i++)
			{
				// Last slice takes into account remaining pixels
				int currentWidth = i != sliceCount - 1 ? sliceWidth : input.Width - processedWidth;

				var currentSlice = new Bitmap(currentWidth, input.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
				using(var graphics = Graphics.FromImage(currentSlice))
				{
					graphics.DrawImage(input, -processedWidth, 0);
				}

				processedWidth += currentWidth;

				outputBitmaps[i] = currentSlice;
			}

			return outputBitmaps;
		}

		/// <summary>Computes the histogram of a bitpam.</summary>
		/// <param name="input">The bitmap.</param>
		/// <returns>The histogram.</returns>
		public static Histogram ComputeHistogram(Bitmap input)
		{
			// This method is inefficient (GetPixel is slow) but easy to understand

			var result = new Histogram(input.Width * input.Height);
			double increment = 1D / result.TotalPixels;

			for(int row = 0; row < input.Height; row++)
			{
				for(int col = 0; col < input.Width; col++)
				{
					Color pixel = input.GetPixel(col, row);
					int grayScale = (int)Math.Round(pixel.R * 0.3F + pixel.G * 0.59F + pixel.B * 0.11F);

					// Make sure the result is inside the freqs array (0-255)
					if(grayScale < 0) grayScale = 0;
					if(grayScale > Histogram.FrequenciesSize - 1) grayScale = Histogram.FrequenciesSize - 1;

					result.Frequencies[grayScale] += increment;
				}
			}
			
			return result;
		}

		/// <summary>Merges two histograms.</summary>
		/// <param name="hist1">The first histogram.</param>
		/// <param name="hist2">The second histogram.</param>
		/// <returns>The merged histogram.</returns>
		public static Histogram MergeHistograms(Histogram hist1, Histogram hist2)
		{
			var result = new Histogram(hist1.TotalPixels + hist2.TotalPixels);
			
			for(int i = 0; i < result.Frequencies.Length; i++)
			{
				result.Frequencies[i] =
					(hist1.Frequencies[i] * hist1.TotalPixels + hist2.Frequencies[i] * hist2.TotalPixels) / (double)result.TotalPixels;
			}

			return result;
		}

		/// <summary>Gets the map/reduce functions.</summary>
		/// <returns>The functions.</returns>
		public static MapReduceFunctions GetMapReduceFunctions()
		{
			return new MapReduceFunctions()
				{
					Mapper = (Func<Bitmap, Histogram>)ComputeHistogram,
					Reducer = (Func<Histogram, Histogram, Histogram>)MergeHistograms,
					Aggregator = (Func<Histogram, Histogram, Histogram>)MergeHistograms
				};
		}
	}

}