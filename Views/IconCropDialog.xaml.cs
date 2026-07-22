using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Launcher.Views
{
    /// <summary>
    /// Простий кроппер іконки без сторонніх NuGet-пакетів - лише вбудований
    /// Windows.Graphics.Imaging (BitmapDecoder/SoftwareBitmap/BitmapEncoder).
    /// Показує зображення у фіксованій області 400x400 (Stretch=Uniform), дає
    /// перетягувати й розтягувати квадратну рамку кропу поверх нього, і на Apply
    /// мапить координати рамки з екранних піксельних координат назад у координати
    /// оригінального файлу, обрізає й зберігає квадратний PNG.
    /// </summary>
    public sealed partial class IconCropDialog : ContentDialog
    {
        private const double AreaSize = 400;
        private const double MinCropSize = 40;

        private readonly string _sourcePath;
        private readonly string _destPath;

        private uint _sourcePixelWidth;
        private uint _sourcePixelHeight;

        // Межі, у яких насправді намальована картинка всередині 400x400-контейнера
        // (через Stretch=Uniform картинка може не займати весь квадрат - лишається "letterbox").
        private double _imgOffsetX, _imgOffsetY, _imgDisplayWidth, _imgDisplayHeight;

        private bool _isDraggingMove;
        private bool _isDraggingResize;
        private Windows.Foundation.Point _dragStart;
        private double _rectStartLeft, _rectStartTop, _rectStartSize;

        public string? ResultPath { get; private set; }

        public IconCropDialog(string sourcePath, string destPath)
        {
            this.InitializeComponent();
            _sourcePath = sourcePath;
            _destPath = destPath;

            // WinUI3 не має ClipToBounds (WPF-властивість) - обрізання вмісту за межі
            // 400x400-контейнера задаємо явною Clip-геометрією. Розмір фіксований,
            // тож можна поставити один раз тут, без прив'язки до SizeChanged.
            CropArea.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, AreaSize, AreaSize) };

            this.Opened += IconCropDialog_Opened;
            this.PrimaryButtonClick += IconCropDialog_PrimaryButtonClick;
        }

        private async void IconCropDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            var file = await StorageFile.GetFileFromPathAsync(_sourcePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            _sourcePixelWidth = decoder.PixelWidth;
            _sourcePixelHeight = decoder.PixelHeight;

            var bitmap = new BitmapImage();
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            SourceImage.Source = bitmap;

            ComputeDisplayBounds();
            InitCropRect();
            UpdateOverlay();
        }

        /// <summary>Рахує, де саме в межах 400x400 намальована картинка (Stretch=Uniform центрує й летербоксить).</summary>
        private void ComputeDisplayBounds()
        {
            double srcRatio = (double)_sourcePixelWidth / _sourcePixelHeight;
            double areaRatio = AreaSize / AreaSize; // 1.0, контейнер квадратний

            if (srcRatio > areaRatio)
            {
                _imgDisplayWidth = AreaSize;
                _imgDisplayHeight = AreaSize / srcRatio;
            }
            else
            {
                _imgDisplayHeight = AreaSize;
                _imgDisplayWidth = AreaSize * srcRatio;
            }

            _imgOffsetX = (AreaSize - _imgDisplayWidth) / 2;
            _imgOffsetY = (AreaSize - _imgDisplayHeight) / 2;
        }

        /// <summary>Стартова рамка - найбільший квадрат, що вписується в показане зображення, по центру.</summary>
        private void InitCropRect()
        {
            double size = Math.Min(_imgDisplayWidth, _imgDisplayHeight);
            double left = _imgOffsetX + (_imgDisplayWidth - size) / 2;
            double top = _imgOffsetY + (_imgDisplayHeight - size) / 2;

            SetCropRect(left, top, size);
        }

        private void SetCropRect(double left, double top, double size)
        {
            // Клемп у межі показаного зображення.
            size = Math.Clamp(size, MinCropSize, Math.Min(_imgDisplayWidth, _imgDisplayHeight));
            left = Math.Clamp(left, _imgOffsetX, _imgOffsetX + _imgDisplayWidth - size);
            top = Math.Clamp(top, _imgOffsetY, _imgOffsetY + _imgDisplayHeight - size);

            Canvas.SetLeft(CropRect, left);
            Canvas.SetTop(CropRect, top);
            CropRect.Width = size;
            CropRect.Height = size;
        }

        /// <summary>Перемальовує затемнення навколо рамки і позицію кутика-ресайзера під поточний CropRect.</summary>
        private void UpdateOverlay()
        {
            double left = Canvas.GetLeft(CropRect);
            double top = Canvas.GetTop(CropRect);
            double size = CropRect.Width;

            DimTop.Width = AreaSize; DimTop.Height = Math.Max(0, top);
            Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);

            DimBottom.Width = AreaSize; DimBottom.Height = Math.Max(0, AreaSize - (top + size));
            Canvas.SetLeft(DimBottom, 0); Canvas.SetTop(DimBottom, top + size);

            DimLeft.Width = Math.Max(0, left); DimLeft.Height = size;
            Canvas.SetLeft(DimLeft, 0); Canvas.SetTop(DimLeft, top);

            DimRight.Width = Math.Max(0, AreaSize - (left + size)); DimRight.Height = size;
            Canvas.SetLeft(DimRight, left + size); Canvas.SetTop(DimRight, top);

            Canvas.SetLeft(ResizeHandle, left + size - ResizeHandle.Width / 2);
            Canvas.SetTop(ResizeHandle, top + size - ResizeHandle.Height / 2);
        }

        private void CropRect_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingMove = true;
            _dragStart = e.GetCurrentPoint(OverlayCanvas).Position;
            _rectStartLeft = Canvas.GetLeft(CropRect);
            _rectStartTop = Canvas.GetTop(CropRect);
            ((UIElement)sender).CapturePointer(e.Pointer);
        }

        private void ResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingResize = true;
            _dragStart = e.GetCurrentPoint(OverlayCanvas).Position;
            _rectStartSize = CropRect.Width;
            _rectStartLeft = Canvas.GetLeft(CropRect);
            _rectStartTop = Canvas.GetTop(CropRect);
            ((UIElement)sender).CapturePointer(e.Pointer);
        }

        private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(OverlayCanvas).Position;

            if (_isDraggingMove)
            {
                double dx = pos.X - _dragStart.X;
                double dy = pos.Y - _dragStart.Y;
                SetCropRect(_rectStartLeft + dx, _rectStartTop + dy, CropRect.Width);
                UpdateOverlay();
            }
            else if (_isDraggingResize)
            {
                // Тягнемо за правий-нижній кут - розмір міняється разом по діагоналі,
                // лівий-верхній кут (left/top) лишається на місці.
                double delta = ((pos.X - _dragStart.X) + (pos.Y - _dragStart.Y)) / 2;
                SetCropRect(_rectStartLeft, _rectStartTop, _rectStartSize + delta);
                UpdateOverlay();
            }
        }

        private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingMove = false;
            _isDraggingResize = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        }

        private async void IconCropDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                await CropAndSaveAsync();
                ResultPath = _destPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося обрізати іконку: {ex.Message}");
                ResultPath = null;
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async Task CropAndSaveAsync()
        {
            // Мапимо рамку кропу з екранних координат (400x400, з урахуванням letterbox-зсуву
            // від Stretch=Uniform) назад у піксельні координати оригінального файлу.
            double left = Canvas.GetLeft(CropRect) - _imgOffsetX;
            double top = Canvas.GetTop(CropRect) - _imgOffsetY;
            double size = CropRect.Width;

            double scale = _sourcePixelWidth / _imgDisplayWidth;

            uint srcX = (uint)Math.Max(0, Math.Round(left * scale));
            uint srcY = (uint)Math.Max(0, Math.Round(top * scale));
            uint srcSize = (uint)Math.Round(size * scale);

            // Клемп, щоб через округлення не вилізти за межі оригіналу.
            srcSize = Math.Min(srcSize, Math.Min(_sourcePixelWidth - srcX, _sourcePixelHeight - srcY));

            var sourceFile = await StorageFile.GetFileFromPathAsync(_sourcePath);
            using var inputStream = await sourceFile.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(inputStream);

            var transform = new BitmapTransform
            {
                Bounds = new BitmapBounds { X = srcX, Y = srcY, Width = srcSize, Height = srcSize }
            };

            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_destPath)!);

            var destFolder = await StorageFolder.GetFolderFromPathAsync(System.IO.Path.GetDirectoryName(_destPath)!);
            var destFile = await destFolder.CreateFileAsync(System.IO.Path.GetFileName(_destPath), CreationCollisionOption.ReplaceExisting);

            using var outputStream = await destFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync();
        }
    }
}
