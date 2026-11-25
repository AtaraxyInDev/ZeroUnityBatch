using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Zub;

namespace Zub.App
{
    /// <summary>Diálogo jerárquico con el estado de las herramientas de Android.</summary>
    public static class ToolsDialog
    {
        private static readonly Color Verde = Color.Parse("#3DD68C");
        private static readonly Color Rojo = Color.Parse("#F26D6D");
        private static readonly Color Gris = Color.Parse("#8A8A98");

        public static async Task ShowAsync(Window owner)
        {
            var lista = AndroidTools.Detalle();

            var win = new Window
            {
                Title = "Herramientas de Android",
                Width = 680,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#1E1E23"))
            };

            var raiz = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 14 };

            // ---------- cabecera ----------
            var cabecera = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var titulos = new StackPanel { Spacing = 3 };
            titulos.Children.Add(new TextBlock
            {
                Text = "Herramientas de Android",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#EDEDF2"))
            });
            titulos.Children.Add(new TextBlock
            {
                Text = AndroidTools.Portatil
                    ? "Modo portátil: todo viaja en la carpeta tools\\ del programa"
                    : "Usando herramientas instaladas en el sistema",
                FontSize = 12,
                Foreground = new SolidColorBrush(Gris)
            });
            cabecera.Children.Add(titulos);

            int ok = lista.Count(x => x.Ok);
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#22222A")),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(12, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = ok == lista.Count ? "Todo listo" : $"{ok} de {lista.Count}",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(ok == lista.Count ? Verde : Color.Parse("#E8C07A"))
                }
            };
            Grid.SetColumn(badge, 1);
            cabecera.Children.Add(badge);
            raiz.Children.Add(cabecera);

            // ---------- grupos ----------
            foreach (var grupo in lista.GroupBy(x => x.Grupo))
            {
                var bloque = new StackPanel { Spacing = 6 };

                bloque.Children.Add(new TextBlock
                {
                    Text = grupo.Key.ToUpperInvariant(),
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Gris),
                    LetterSpacing = 1
                });

                var tarjeta = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#17171C")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#2C2C34")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10)
                };

                var filas = new StackPanel { Spacing = 10 };

                foreach (var h in grupo)
                {
                    var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

                    var punto = new TextBlock
                    {
                        Text = h.Ok ? "●" : "○",
                        FontSize = 13,
                        Margin = new Thickness(0, 1, 10, 0),
                        Foreground = new SolidColorBrush(h.Ok ? Verde : (h.Opcional ? Gris : Rojo))
                    };
                    fila.Children.Add(punto);

                    var textos = new StackPanel { Spacing = 2 };

                    var nombre = new TextBlock
                    {
                        Text = h.Nombre,
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#E4E4EC"))
                    };
                    textos.Children.Add(nombre);

                    textos.Children.Add(new TextBlock
                    {
                        Text = h.Ok ? h.Ruta : (h.Opcional ? "no instalado (opcional)" : "no encontrado"),
                        FontSize = 11.5,
                        FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                        Foreground = new SolidColorBrush(h.Ok ? Color.Parse("#9A9AA8") : Gris),
                        TextWrapping = TextWrapping.Wrap
                    });

                    if (!string.IsNullOrEmpty(h.Nota))
                    {
                        textos.Children.Add(new TextBlock
                        {
                            Text = h.Nota,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#75757F"))
                        });
                    }

                    Grid.SetColumn(textos, 1);
                    fila.Children.Add(textos);

                    var estado = new TextBlock
                    {
                        Text = h.Ok ? "OK" : (h.Opcional ? "—" : "FALTA"),
                        FontSize = 10.5,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(8, 2, 0, 0),
                        Foreground = new SolidColorBrush(h.Ok ? Verde : (h.Opcional ? Gris : Rojo))
                    };
                    Grid.SetColumn(estado, 2);
                    fila.Children.Add(estado);

                    filas.Children.Add(fila);
                }

                tarjeta.Child = filas;
                bloque.Children.Add(tarjeta);
                raiz.Children.Add(bloque);
            }

            // ---------- pie ----------
            var pie = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            pie.Children.Add(new TextBlock
            {
                Text = "Carpeta: " + AndroidTools.ToolsDir,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                Foreground = new SolidColorBrush(Color.Parse("#75757F")),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            var boton = new Button
            {
                Content = "Entendido",
                Padding = new Thickness(18, 7),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            boton.Classes.Add("accent");
            boton.Click += (s, e) => win.Close();

            Grid.SetColumn(boton, 1);
            pie.Children.Add(boton);
            raiz.Children.Add(pie);

            win.Content = raiz;
            await win.ShowDialog(owner);
        }
    }
}
