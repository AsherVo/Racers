using Vo;

namespace MintECS;

public abstract class Message 
{
    public static implicit operator bool ( Message message )
        => message != null;

    public override string ToString ()
    	=> Utils.ObjectToString( this );
}