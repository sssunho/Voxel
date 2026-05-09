// 3D array. 원소 접근 시 경계를 확인하지 않습니다.
public class FlatArray3D<T>
{
    T[] _data;

    int _width;
    int _height;
    int _depth;
    int _strideY;
    int _strideZ;

    public FlatArray3D(int width, int height, int depth)
    {
        _width = width;
        _height = height;
        _depth = depth;

        _strideY = width;
        _strideZ = width * height;

        _data = new T[_width * _height * _depth];
    }

    public ref T this[int x, int y, int z]
    {
        get
        {
            return ref _data[x + y * _strideY + z * _strideZ];
        }
    }
}
