using System;
using System.Collections.Generic;
using UnityEngine;

// Token: 0x02000212 RID: 530
public static class ArrayUtils {
	// Token: 0x060008DC RID: 2268 RVA: 0x00034FB4 File Offset: 0x000333B4
	public static bool IsEmpty<T>(this T[] _array) {
		return _array.Length == 0;
	}

	// Token: 0x060008DD RID: 2269 RVA: 0x00034FBC File Offset: 0x000333BC
	public static void SetValues<T>(this T[] _array, T _value, int _start, int _end) {
		for (int i = _start; i < _end; i++) {
			_array[i] = _value;
		}
	}

	// Token: 0x060008DE RID: 2270 RVA: 0x00034FE4 File Offset: 0x000333E4
	public static T[] Union<T>(this T[] _a, T[] _b) {
		T[] array = new T[_a.Length + _b.Length];
		_a.CopyTo(array, 0);
		_b.CopyTo(array, _a.Length);
		return array;
	}

	// Token: 0x060008DF RID: 2271 RVA: 0x00035014 File Offset: 0x00033414
	public static T[] Compliment<T>(this T[] _a, T[] _b) {
		Predicate<T> match = (T _v) => !_b.Contains(_v);
		return _a.FindAll(match);
	}

	// Token: 0x060008E0 RID: 2272 RVA: 0x00035042 File Offset: 0x00033442
	public static void ExpandingAssign<T>(ref T[] _array, int _index, T _value) {
		if (_index >= _array.Length) {
			Array.Resize<T>(ref _array, _index + 1);
		}
		_array[_index] = _value;
	}

	// Token: 0x060008E1 RID: 2273 RVA: 0x00035060 File Offset: 0x00033460
	public static T TryAtIndex<T>(this T[] _array, int _index) {
		return _array.TryAtIndex(_index, default(T));
	}

	// Token: 0x060008E2 RID: 2274 RVA: 0x0003507D File Offset: 0x0003347D
	public static T TryAtIndex<T>(this T[] _array, int _index, T _default) {
		if (_index >= _array.Length || _index < 0) {
			return _default;
		}
		return _array[_index];
	}

	// Token: 0x060008E3 RID: 2275 RVA: 0x00035098 File Offset: 0x00033498
	public static void SafeSet<T>(this T[] _array, int _index, T _value) {
		if (_index >= 0 && _index < _array.Length) {
			_array[_index] = _value;
		}
	}

	// Token: 0x060008E4 RID: 2276 RVA: 0x000350B2 File Offset: 0x000334B2
	public static U[] ConvertAll<T, U>(this T[] _array, Converter<T, U> _converter) {
		return Array.ConvertAll<T, U>(_array, _converter);
	}

	// Token: 0x060008E5 RID: 2277 RVA: 0x000350BC File Offset: 0x000334BC
	public static U[] ConvertAll<T, U>(this T[] _array, Generic<U, int, T> _converter) {
		U[] array = new U[_array.Length];
		for (int i = 0; i < array.Length; i++) {
			array[i] = _converter(i, _array[i]);
		}
		return array;
	}

	// Token: 0x060008E6 RID: 2278 RVA: 0x000350FC File Offset: 0x000334FC
	public static U[] ConvertRange<T, U>(this T[] _array, Generic<U, int, T> _converter, int _start, int _length) {
		U[] array = new U[_length];
		for (int i = 0; i < _length; i++) {
			int num = i + _start;
			array[i] = _converter(num, _array[num]);
		}
		return array;
	}

	// Token: 0x060008E7 RID: 2279 RVA: 0x0003513C File Offset: 0x0003353C
	public static T[] Intersection<T>(this T[] _a, T[] _b) where T : IComparable, IConvertible {
		T[] array = new T[0];
		bool[] array2 = new bool[_b.Length];
		for (int i = 0; i < _a.Length; i++) {
			for (int j = 0; j < _b.Length; j++) {
				if (!array2[j] && _b[j].Equals(_a[i])) {
					array2[j] = true;
					Array.Resize<T>(ref array, array.Length + 1);
					array[array.Length - 1] = _a[i];
					break;
				}
			}
		}
		return array;
	}

	// Token: 0x060008E8 RID: 2280 RVA: 0x000351D8 File Offset: 0x000335D8
	public static bool Contains<T>(this T[] _array, T _value) {
		return _array.FindIndex_Predicate((T x) => _value.Equals(x)) != -1;
	}

	// Token: 0x060008E9 RID: 2281 RVA: 0x0003520A File Offset: 0x0003360A
	public static bool Contains<T>(this T[] _array, Predicate<T> _matchFunction) {
		return _array.FindIndex_Predicate(_matchFunction) != -1;
	}

	// Token: 0x060008EA RID: 2282 RVA: 0x00035219 File Offset: 0x00033619
	public static bool Contains<T>(this T[] _array, Generic<bool, int, T> _matchFunction) {
		return _array.FindIndex_Generic(_matchFunction) != -1;
	}

	// Token: 0x060008EC RID: 2284 RVA: 0x000352B0 File Offset: 0x000336B0
	public static int FindIndex<T>(this T[] _array, T _value) where T : IComparable {
		for (int i = 0; i < _array.Length; i++) {
			if (_value.Equals(_array[i])) {
				return i;
			}
		}
		return -1;
	}

	// Token: 0x060008ED RID: 2285 RVA: 0x000352F2 File Offset: 0x000336F2
	public static int FindIndex_Predicate<T>(this T[] _array, Predicate<T> _matchFunction) {
		return Array.FindIndex<T>(_array, _matchFunction);
	}

	// Token: 0x060008EE RID: 2286 RVA: 0x000352FC File Offset: 0x000336FC
	public static int FindIndex_Generic<T>(this T[] _array, Generic<bool, int, T> _matchFunction) {
		for (int i = 0; i < _array.Length; i++) {
			if (_matchFunction(i, _array[i])) {
				return i;
			}
		}
		return -1;
	}

	// Token: 0x060008EF RID: 2287 RVA: 0x00035334 File Offset: 0x00033734
	public static void PushBack<T>(ref T[] _array, T _value) {
		int num = _array.Length;
		Array.Resize<T>(ref _array, num + 1);
		_array[num] = _value;
	}

	// Token: 0x060008F0 RID: 2288 RVA: 0x00035358 File Offset: 0x00033758
	public static void Insert<T>(ref T[] _array, int _index, T _value) {
		int num = _array.Length;
		Array.Resize<T>(ref _array, 1 + num);
		for (int i = num; i > _index; i--) {
			_array[i] = _array[i - 1];
		}
		_array[_index] = _value;
	}

	// Token: 0x060008F1 RID: 2289 RVA: 0x000353A0 File Offset: 0x000337A0
	public static void RemoveAllDuplicates<T>(ref T[] _array) {
		for (int i = _array.Length - 1; i >= 0; i--) {
			T e = _array[i];
			if (_array.FindAll((T x) => x.Equals(e)).Length > 1) {
				ArrayUtils.RemoveAt<T>(ref _array, i);
			}
		}
	}

	// Token: 0x060008F2 RID: 2290 RVA: 0x000353FC File Offset: 0x000337FC
	public static void RemoveAt<T>(ref T[] _array, int _index) {
		int num = _array.Length;
		for (int i = _index + 1; i < num; i++) {
			_array[i - 1] = _array[i];
		}
		Array.Resize<T>(ref _array, num - 1);
	}

	// Token: 0x060008F3 RID: 2291 RVA: 0x00035440 File Offset: 0x00033840
	public static KeyValuePair<int, T> FindLowestScoring<T>(this T[] _array, Generic<float, T> _scoreFunction) {
		float num;
		return _array.FindLowestScoring(_scoreFunction, out num);
	}

	// Token: 0x060008F4 RID: 2292 RVA: 0x00035458 File Offset: 0x00033858
	public static KeyValuePair<int, T> FindLowestScoring<T>(this T[] _array, Generic<float, T> _scoreFunction, out float o_score) {
		o_score = float.MaxValue;
		int num = -1;
		for (int i = 0; i < _array.Length; i++) {
			float num2 = _scoreFunction(_array[i]);
			if (num2 < o_score) {
				o_score = num2;
				num = i;
			}
		}
		if (num == -1) {
			return new KeyValuePair<int, T>(num, default(T));
		}
		return new KeyValuePair<int, T>(num, _array[num]);
	}

	// Token: 0x060008F5 RID: 2293 RVA: 0x000354C4 File Offset: 0x000338C4
	public static KeyValuePair<int, T> FindHighestScoring<T>(this T[] _array, Generic<float, T> _scoreFunction) {
		float num;
		return _array.FindHighestScoring(_scoreFunction, out num);
	}

	// Token: 0x060008F6 RID: 2294 RVA: 0x000354DC File Offset: 0x000338DC
	public static KeyValuePair<int, T> FindHighestScoring<T>(this T[] _array, Generic<float, T> _scoreFunction, out float o_score) {
		o_score = float.MinValue;
		int num = -1;
		for (int i = 0; i < _array.Length; i++) {
			float num2 = _scoreFunction(_array[i]);
			if (num2 > o_score) {
				o_score = num2;
				num = i;
			}
		}
		if (num == -1) {
			return new KeyValuePair<int, T>(num, default(T));
		}
		return new KeyValuePair<int, T>(num, _array[num]);
	}

	// Token: 0x060008FA RID: 2298 RVA: 0x00035634 File Offset: 0x00033A34
	public static T[] FindAll<T>(this T[] _array, Predicate<T> _match) {
		T[] result = new T[0];
		for (int i = 0; i < _array.Length; i++) {
			if (_match(_array[i])) {
				ArrayUtils.PushBack<T>(ref result, _array[i]);
			}
		}
		return result;
	}

	// Token: 0x060008FB RID: 2299 RVA: 0x00035680 File Offset: 0x00033A80
	public static T[] AllRemoved_Generic<T>(this T[] _array, Generic<bool, int, T> _match) {
		List<T> list = new List<T>(_array);
		for (int i = list.Count - 1; i >= 0; i--) {
			if (_match(i, list[i])) {
				list.RemoveAt(i);
			}
		}
		return list.ToArray();
	}

	// Token: 0x060008FC RID: 2300 RVA: 0x000356D0 File Offset: 0x00033AD0
	public static T[] AllRemoved_Predicate<T>(this T[] _array, Predicate<T> _match) {
		List<T> list = new List<T>(_array);
		list.RemoveAll(_match);
		return list.ToArray();
	}

	// Token: 0x060008FD RID: 2301 RVA: 0x000356F4 File Offset: 0x00033AF4
	public static string Concatenated<T>(this T[] _array, Generic<string, int, T> _converter) {
		string text = string.Empty;
		for (int i = 0; i < _array.Length; i++) {
			text += _converter(i, _array[i]);
		}
		return text;
	}

	// Token: 0x060008FE RID: 2302 RVA: 0x00035734 File Offset: 0x00033B34
	public static Vector3 Mean<T>(this T[] _array, Generic<Vector3, T> _converter) {
		Vector3 vector = Vector3.zero;
		if (_array.Length > 0) {
			for (int i = 0; i < _array.Length; i++) {
				vector += _converter(_array[i]);
			}
			return vector / (float) _array.Length;
		}
		return vector;
	}

	// Token: 0x060008FF RID: 2303 RVA: 0x00035784 File Offset: 0x00033B84
	public static U Collapse<T, U>(this T[] _array, Generic<U, T, U> _converter) {
		U u = default(U);
		for (int i = 0; i < _array.Length; i++) {
			u = _converter(_array[i], u);
		}
		return u;
	}

	// Token: 0x06000902 RID: 2306 RVA: 0x00035838 File Offset: 0x00033C38
	public static string Stringify<T>(this T[] _array) {
		string str = "{ ";
		str = str + _array.Collapse(new Generic<string, T, string>(ArrayUtils.AssembleString<T>)) + " }";
		return str + " }";
	}

	// Token: 0x06000903 RID: 2307 RVA: 0x00035874 File Offset: 0x00033C74
	public static string AssembleString<T>(T _v, string _s) {
		return _s + ", " + _v.ToString();
	}
}