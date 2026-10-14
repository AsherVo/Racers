namespace MintECS;

public interface ISystem
{
	public void StartSystem ( World world );
	public void StopSystem ( World world );
	public void TickSystem ( World world );
}