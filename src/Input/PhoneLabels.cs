using PartyAnywhere;

namespace MicroWorksCoop.Input
{
    /// <summary>
    /// O nome de cada ação nos botões do celular, igual ao mapa do PadInput e ao controle do manifesto
    /// (`controls`). Nomes curtos: no celular o botão é pequeno e precisa ser lido de relance. O Hub guarda por vaga enquanto o
    /// jogo está conectado (vale para quem entra depois e para quem cai e volta), então basta mandar uma vez
    /// para todas as vagas. Só a cópia 1 manda: é ela que está conectada ao Hub.
    /// </summary>
    static class PhoneLabels
    {
        static readonly (Buttons button, string label)[] labels =
        {
            (Buttons.LabelStickL, "Andar"),
            (Buttons.LabelStickR, "Mirar"),
            (Buttons.A, "Pular"),
            (Buttons.B, "Agachar"),
            (Buttons.X, "Usar"),
            (Buttons.Y, "Reações"),
            (Buttons.LB, "Correr"),
            (Buttons.RB, "Empurrar"),
            (Buttons.LabelRT, "Atirar"),
            (Buttons.LabelLT, "Tiro 2"),
            (Buttons.RS, "Frente"),
            (Buttons.LS, "Extra"),
            (Buttons.Up, "Votar"), // os direcionais dividem um nome só
            (Buttons.Back, "Jogadores"),
            (Buttons.Start, "Pausa"),
        };

        public static void Send(Client hub, int seat)
        {
            foreach (var (button, label) in labels) hub.SetLabel(seat, button, label);
        }
    }
}
